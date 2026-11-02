using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FastGithub.UI
{
    /// <summary>
    /// FlowChart.xaml 的交互逻辑
    /// </summary>
    public partial class FlowChart : UserControl
    {
        private readonly IList<RateTick> readValues = new List<RateTick>();

        private readonly IList<RateTick> writeValues = new List<RateTick>();

        private readonly Polyline readPolyline = new Polyline
        {
            Stroke = new SolidColorBrush(Colors.DodgerBlue),
            StrokeThickness = 1.5
        };

        private readonly Polyline writePolyline = new Polyline
        {
            Stroke = new SolidColorBrush(Colors.IndianRed),
            StrokeThickness = 1.5
        };

        private static DateTime GetDateTime(double timestamp) => new DateTime(1970, 1, 1).Add(TimeSpan.FromMilliseconds(timestamp)).ToLocalTime();

        private static double GetTimestamp(DateTime dateTime) => dateTime.ToUniversalTime().Subtract(new DateTime(1970, 1, 1)).TotalMilliseconds;

        public FlowChart()
        {
            InitializeComponent();

            this.chartCanvas.Children.Add(this.readPolyline);
            this.chartCanvas.Children.Add(this.writePolyline);
            this.chartCanvas.SizeChanged += (s, e) => this.Redraw();

            this.InitFlowChartAsync();
        }

        private async void InitFlowChartAsync()
        {
            using var httpClient = new HttpClient();
            while (this.Dispatcher.HasShutdownStarted == false)
            {
                try
                {
                    await this.FlushFlowStatisticsAsync(httpClient);
                }
                catch (Exception)
                {
                }
                finally
                {
                    await Task.Delay(TimeSpan.FromSeconds(1d));
                }
            }
        }

        private async Task FlushFlowStatisticsAsync(HttpClient httpClient)
        {
            var response = await httpClient.GetAsync("http://localhost/flowStatistics");
            var json = await response.EnsureSuccessStatusCode().Content.ReadAsStringAsync();
            var flowStatistics = JsonConvert.DeserializeObject<FlowStatistics>(json);
            if (flowStatistics == null)
            {
                return;
            }

            this.textBlockRead.Text = FlowStatistics.ToNetworkSizeString(flowStatistics.TotalRead);
            this.textBlockWrite.Text = FlowStatistics.ToNetworkSizeString(flowStatistics.TotalWrite);

            var timestamp = GetTimestamp(DateTime.Now);
            this.readValues.Add(new RateTick(flowStatistics.ReadRate, timestamp));
            this.writeValues.Add(new RateTick(flowStatistics.WriteRate, timestamp));

            if (this.readValues.Count > 60)
            {
                this.readValues.RemoveAt(0);
                this.writeValues.RemoveAt(0);
            }

            this.Redraw();
        }

        private void Redraw()
        {
            var width = this.chartCanvas.ActualWidth;
            var height = this.chartCanvas.ActualHeight;
            this.readPolyline.Points.Clear();
            this.writePolyline.Points.Clear();
            if (width <= 0 || height <= 0 || this.readValues.Count == 0)
            {
                return;
            }

            var maxRate = Math.Max(1d, Math.Max(this.readValues.Max(t => t.Rate), this.writeValues.Max(t => t.Rate)) * 1.1);
            var count = this.readValues.Count;
            for (var i = 0; i < count; i++)
            {
                var x = count == 1 ? 0 : (double)i / (count - 1) * width;
                var yRead = height - (this.readValues[i].Rate / maxRate) * height;
                var yWrite = height - (this.writeValues[i].Rate / maxRate) * height;
                this.readPolyline.Points.Add(new Point(x, yRead));
                this.writePolyline.Points.Add(new Point(x, yWrite));
            }
        }

        private class RateTick
        {
            public double Rate { get; }

            public double Timestamp { get; }

            public RateTick(double rate, double timestamp)
            {
                this.Rate = rate;
                this.Timestamp = timestamp;
            }
        }
    }
}
