using GPIBKeithleyCurrentMeasurement.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace GPIBKeithleyCurrentMeasurement
{
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;

        public SettingsWindow()
        {
            InitializeComponent();
            _settings = AppSettings.Instance;
            ResourceNameTextBox.Text = _settings.GpibResourceName;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            _settings.GpibResourceName = ResourceNameTextBox.Text;
            _settings.Save();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
