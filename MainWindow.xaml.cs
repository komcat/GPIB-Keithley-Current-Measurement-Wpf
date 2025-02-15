using GPIBKeithleyCurrentMeasurement.Settings;
using System;
using System.Windows;
using System.Windows.Threading;

namespace GPIBKeithleyCurrentMeasurement
{
    public partial class MainWindow : Window
    {
        private readonly GpibService _gpibService;
        private readonly System.Diagnostics.Stopwatch _stopwatch;

        public MainWindow()
        {
            InitializeComponent();

            _gpibService = new GpibService();
            _gpibService.MeasurementReceived += OnMeasurementReceived;
            _gpibService.ErrorOccurred += OnErrorOccurred;

            _stopwatch = new System.Diagnostics.Stopwatch();
            
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_gpibService.IsConnected)
                {
                    ConnectButton.IsEnabled = false;
                    StatusText.Text = "Connecting...";

                    await _gpibService.ConnectAsync();

                    ConnectButton.Content = "Disconnect";
                    StartButton.IsEnabled = true;
                    StatusText.Text = "Connected";
                }
                else
                {
                    StopMeasurement();
                    _gpibService.Disconnect();
                    ConnectButton.Content = "Connect";
                    StartButton.IsEnabled = false;
                    StatusText.Text = "Disconnected";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connection error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Connection failed";
            }
            finally
            {
                ConnectButton.IsEnabled = true;
            }
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await StartMeasurementAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting measurement: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StopMeasurement();
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopMeasurement();
        }

        private async Task StartMeasurementAsync()
        {
            OutputTextBox.Clear();
            _stopwatch.Restart();

            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            ConnectButton.IsEnabled = false;
            StatusText.Text = "Reading measurements...";

            // Start continuous measurement for 10 seconds
            await _gpibService.StartContinuousReadAsync(10);

            // After measurement completes
            StopMeasurement();
            OutputTextBox.AppendText("Measurement complete\r\n");
        }

        private void StopMeasurement()
        {
            _gpibService.StopMeasurement();
            _stopwatch.Stop();

            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            ConnectButton.IsEnabled = true;
            StatusText.Text = "Connected";
        }

        private void OnMeasurementReceived(object sender, string measurement)
        {
            // Ensure we're on the UI thread
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => OnMeasurementReceived(sender, measurement));
                return;
            }

            OutputTextBox.AppendText($"Time: {_stopwatch.ElapsedMilliseconds}ms - Reading: {measurement}\r\n");
            OutputTextBox.ScrollToEnd();
        }

        private void OnErrorOccurred(object sender, Exception ex)
        {
            // Ensure we're on the UI thread
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => OnErrorOccurred(sender, ex));
                return;
            }

            MessageBox.Show($"Error during reading: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StopMeasurement();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _gpibService.Dispose();
            base.OnClosing(e);
        }

        
    }
}