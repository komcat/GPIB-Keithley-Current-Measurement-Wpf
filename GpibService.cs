using System;
using System.IO;
using System.Threading.Tasks;
using NationalInstruments.Visa;
using Serilog;

namespace GPIBKeithleyCurrentMeasurement
{
    public class GpibService : IDisposable
    {
        private MessageBasedSession _session;
        
        private readonly string _resourceName;
        private bool _isConnected;
        private bool _isMeasuring;
        private const int MAX_RETRY_ATTEMPTS = 10;
        private const int CONNECTION_TIMEOUT_MS = 5000;
        private const int VALIDATION_DELAY_MS = 250;
        public bool IsConnected => _isConnected;
        public bool IsMeasuring => _isMeasuring;

        public event EventHandler<string> MeasurementReceived;
        public event EventHandler<Exception> ErrorOccurred;
        
        public GpibService(string resourceName = "GPIB0::1::INSTR")
        {
            _resourceName = resourceName;
        }

        private async Task<bool> ValidateConnectionAsync()
        {
            try
            {
                // Add a small delay to ensure device is ready
                await Task.Delay(VALIDATION_DELAY_MS);

                // Try to perform a basic communication test with proper timeout handling
                using var cts = new CancellationTokenSource(CONNECTION_TIMEOUT_MS);

                return await Task.Run(async () =>
                {
                    try
                    {
                        // Store original timeout
                        int originalTimeout = _session.TimeoutMilliseconds;

                        try
                        {
                            // Set a shorter timeout for the validation
                            _session.TimeoutMilliseconds = 2000; // 2 seconds timeout for validation

                            // Send identification query
                            _session.RawIO.Write("*IDN?\n");
                            string response = _session.RawIO.ReadString().Trim();

                            // Check if we got a valid response
                            if (string.IsNullOrEmpty(response))
                            {
                                Log.Warning("Device returned empty response during validation");
                                return false;
                            }

                            Log.Information($"Device identification: {response}");
                            return true;
                        }
                        finally
                        {
                            // Restore original timeout
                            _session.TimeoutMilliseconds = originalTimeout;
                        }
                    }
                    catch (Ivi.Visa.IOTimeoutException ex)
                    {
                        Log.Warning($"Timeout during device validation: {ex.Message}");
                        return false;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"Error during device validation: {ex.Message}");
                        return false;
                    }
                }, cts.Token);
            }
            catch (OperationCanceledException)
            {
                Log.Warning("Validation operation was cancelled due to timeout");
                return false;
            }
            catch (Exception ex)
            {
                Log.Warning($"Unexpected error during validation: {ex.Message}");
                return false;
            }
        }
        public async Task ConnectAsync()
        {
            int attemptCount = 0;
            Exception lastException = null;

            while (attemptCount < MAX_RETRY_ATTEMPTS)
            {
                try
                {
                    attemptCount++;
                    Log.Information($"Attempting to connect to GPIB device (Attempt {attemptCount}/{MAX_RETRY_ATTEMPTS})");

                    var rmSession = new ResourceManager();
                    _session = (MessageBasedSession)rmSession.Open(_resourceName);

                    // Validate the connection
                    if (await ValidateConnectionAsync())
                    {
                        _isConnected = true;
                        Log.Information("Successfully connected and validated GPIB device connection");
                        return;
                    }

                    // If validation fails, clean up and try again
                    _session?.Dispose();
                    _session = null;

                    if (attemptCount < MAX_RETRY_ATTEMPTS)
                    {
                        // Add exponential backoff delay before retrying
                        int delayMs = Math.Min(1000 * (int)Math.Pow(2, attemptCount - 1), 5000);
                        await Task.Delay(delayMs);
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    Log.Warning($"Connection attempt {attemptCount} failed: {ex.Message}");

                    if (attemptCount < MAX_RETRY_ATTEMPTS)
                    {
                        // Add exponential backoff delay before retrying
                        int delayMs = Math.Min(1000 * (int)Math.Pow(2, attemptCount - 1), 5000);
                        await Task.Delay(delayMs);
                    }
                }
            }

            _isConnected = false;
            throw new Exception($"Failed to connect to GPIB device after {MAX_RETRY_ATTEMPTS} attempts. Last error: {lastException?.Message}", lastException);
        }

        public async Task StartContinuousReadAsync()
        {
            if (!_isConnected || _session == null)
            {
                throw new InvalidOperationException("Not connected to GPIB device");
            }

            _isMeasuring = true;
            int consecutiveErrorCount = 0;
            const int MAX_CONSECUTIVE_ERRORS = 5;
            const int BASE_RETRY_DELAY_MS = 100;
            const int MAX_RETRY_DELAY_MS = 5000;

            try
            {
                while (_isMeasuring)
                {
                    try
                    {
                        // Send read command
                        await Task.Run(() => _session.RawIO.Write(":READ?\n"));

                        // Read response with timeout handling
                        string measurement = await ReadWithTimeoutAsync();

                        // Reset error count on successful read
                        consecutiveErrorCount = 0;

                        // Raise event with measurement
                        MeasurementReceived?.Invoke(this, measurement);
                    }
                    catch (Exception ex)
                    {
                        consecutiveErrorCount++;

                        // Calculate exponential backoff delay
                        int retryDelay = Math.Min(
                            BASE_RETRY_DELAY_MS * (int)Math.Pow(2, consecutiveErrorCount),
                            MAX_RETRY_DELAY_MS
                        );

                        // Raise error event
                        ErrorOccurred?.Invoke(this, ex);

                        // Stop if max consecutive errors reached
                        if (consecutiveErrorCount >= MAX_CONSECUTIVE_ERRORS)
                        {
                            Log.Error($"Max consecutive errors reached. Stopping measurement. Last error: {ex.Message}");
                            break;
                        }

                        // Wait before retrying
                        await Task.Delay(retryDelay);
                    }

                    // Prevent tight looping
                    await Task.Delay(10);
                }
            }
            finally
            {
                _isMeasuring = false;
            }
        }

        private async Task<string> ReadWithTimeoutAsync(int timeoutMs = 1000)
        {
            const int MAX_RETRIES = 3;
            int retryCount = 0;

            while (retryCount < MAX_RETRIES)
            {
                using var cts = new CancellationTokenSource(timeoutMs);
                try
                {
                    return await Task.Run(() =>
                    {
                        try
                        {
                            return _session.RawIO.ReadString();
                        }
                        catch (Exception ex)
                        {
                            Log.Warning($"Read operation failed: {ex.Message}");
                            throw;
                        }
                    }, cts.Token);
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is IOException)
                {
                    retryCount++;
                    Log.Warning($"Attempt {retryCount} failed: {ex.Message}");

                    if (retryCount < MAX_RETRIES)
                    {
                        Log.Information($"Attempting reconnection, try {retryCount + 1} of {MAX_RETRIES}");

                        try
                        {
                            // Disconnect current session
                            _session?.Dispose();
                            _session = null;
                            _isConnected = false;

                            // Wait before reconnecting
                            await Task.Delay(1000);

                            // Reconnect
                            var rmSession = new ResourceManager();
                            _session = (MessageBasedSession)rmSession.Open(_resourceName);
                            _isConnected = true;

                            // Additional wait after reconnection
                            await Task.Delay(500);

                            Log.Information($"Successfully reconnected on attempt {retryCount + 1}");
                        }
                        catch (Exception reconnectEx)
                        {
                            Log.Error($"Reconnection attempt {retryCount + 1} failed: {reconnectEx.Message}");
                        }
                    }
                    else
                    {
                        Log.Error("Max retry attempts reached");
                        throw new TimeoutException($"Read operation failed after {MAX_RETRIES} reconnection attempts");
                    }
                }
            }

            throw new TimeoutException($"Read operation failed after {MAX_RETRIES} reconnection attempts");
        }
        public async Task StartContinuousReadAsync(int durationSeconds)
        {
            if (!_isConnected || _session == null)
            {
                throw new InvalidOperationException("Not connected to GPIB device");
            }

            _isMeasuring = true;
            var startTime = DateTime.Now;

            try
            {
                while (_isMeasuring && (DateTime.Now - startTime).TotalSeconds < durationSeconds)
                {
                    try
                    {
                        // Send read command
                        await Task.Run(() => _session.RawIO.Write(":READ?\n"));

                        // Read response asynchronously
                        string measurement = await Task.Run(() => _session.RawIO.ReadString());

                        // Raise event with measurement
                        MeasurementReceived?.Invoke(this, measurement);
                    }
                    catch (Exception ex)
                    {
                        ErrorOccurred?.Invoke(this, ex);
                        break;
                    }
                }
            }
            finally
            {
                _isMeasuring = false;
            }
        }

        public void StopMeasurement()
        {
            _isMeasuring = false;
        }

        public void Disconnect()
        {
            StopMeasurement();
            _session?.Dispose();
            _session = null;
            _isConnected = false;
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}