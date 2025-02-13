using System;
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

        public bool IsConnected => _isConnected;
        public bool IsMeasuring => _isMeasuring;

        public event EventHandler<string> MeasurementReceived;
        public event EventHandler<Exception> ErrorOccurred;
        
        public GpibService(string resourceName = "GPIB0::1::INSTR")
        {
            _resourceName = resourceName;
        }

        public async Task ConnectAsync()
        {
            try
            {
                var rmSession = new ResourceManager();
                _session = (MessageBasedSession)rmSession.Open(_resourceName);
                _isConnected = true;
                await Task.Delay(250);
            }
            catch (Exception ex)
            {
                _isConnected = false;
                throw new Exception($"Failed to connect to GPIB device: {ex.Message}");
            }
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
            catch (OperationCanceledException)
            {
                Log.Warning("Read operation timed out");
                throw new TimeoutException("Read operation timed out");
            }
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