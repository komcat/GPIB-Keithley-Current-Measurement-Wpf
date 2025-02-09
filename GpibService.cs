using System;
using System.Threading.Tasks;
using NationalInstruments.Visa;

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

            _isMeasuring = true; // Ensure measurement state is set

            try
            {
                while (_isMeasuring) // Run indefinitely until stopped
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
                        break; // Stop on error
                    }

                    await Task.Delay(10); // Prevent CPU overload (adjust as needed)
                }
            }
            finally
            {
                _isMeasuring = false; // Ensure proper cleanup when stopping
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