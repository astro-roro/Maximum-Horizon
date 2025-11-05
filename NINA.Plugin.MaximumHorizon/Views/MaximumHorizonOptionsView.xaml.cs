using System;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using NINA.Plugin.MaximumHorizon.Models;
using NINA.Plugin.MaximumHorizon.Options;
using NINA.Plugin.MaximumHorizon.Services;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaPoint = System.Windows.Point;

namespace NINA.Plugin.MaximumHorizon.Views
{
    public partial class MaximumHorizonOptionsView : System.Windows.Controls.UserControl
    {
        public MaximumHorizonOptionsView()
        {
            InitializeComponent();
            // DataContext will be set by NINA from the Settings property
            // If it's not set, create a ViewModel wrapper
            if (DataContext == null)
            {
                // Always use shared service instance so SelectedProfileName is shared with conditions
                var svc = MaximumHorizonServiceAccessor.GetShared();
                var options = new MaximumHorizonOptions(svc);
                DataContext = new MaximumHorizonOptionsViewModel(options);
            }
            else if (DataContext is MaximumHorizonOptions optionsObj)
            {
                // Wrap the Options object in a ViewModel for the view
                DataContext = new MaximumHorizonOptionsViewModel(optionsObj);
            }

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MaximumHorizonOptionsViewModel vm)
            {
                vm.PropertyChanged += (s, args) =>
                {
                    if (args.PropertyName == nameof(vm.HorizonPoints))
                    {
                        UpdateHorizonVisualization();
                    }
                };

                if (vm.HorizonPoints is System.Collections.Specialized.INotifyCollectionChanged collection)
                {
                    collection.CollectionChanged += (s, args) => UpdateHorizonVisualization();
                }

                UpdateHorizonVisualization();
            }

            if (HorizonCanvas != null)
            {
                HorizonCanvas.SizeChanged += (s, args) => UpdateHorizonVisualization();
            }
        }

        private void UpdateHorizonVisualization()
        {
            if (HorizonCanvas == null || DataContext is not MaximumHorizonOptionsViewModel vm || vm.HorizonPoints == null)
            {
                return;
            }

            HorizonCanvas.Children.Clear();

            var canvasWidth = HorizonCanvas.ActualWidth > 0 ? HorizonCanvas.ActualWidth : 600;
            var canvasHeight = HorizonCanvas.ActualHeight > 0 ? HorizonCanvas.ActualHeight : 300;
            var padding = 40;
            var plotWidth = canvasWidth - 2 * padding;
            var plotHeight = canvasHeight - 2 * padding;
            var centerX = padding;
            var centerY = canvasHeight - padding;
            var maxAltitude = 90.0;

            // Draw grid lines
            for (int alt = 0; alt <= 90; alt += 15)
            {
                var y = centerY - (alt / maxAltitude) * plotHeight;
                var line = new Line
                {
                    X1 = padding,
                    Y1 = y,
                    X2 = canvasWidth - padding,
                    Y2 = y,
                    Stroke = MediaBrushes.LightGray,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 4, 4 }
                };
                HorizonCanvas.Children.Add(line);

                var label = new TextBlock
                {
                    Text = $"{alt}°",
                    Foreground = MediaBrushes.Gray,
                    FontSize = 10
                };
                Canvas.SetLeft(label, padding - 30);
                Canvas.SetTop(label, y - 8);
                HorizonCanvas.Children.Add(label);
            }

            // Draw azimuth labels
            for (int az = 0; az < 360; az += 45)
            {
                var x = centerX + (az / 360.0) * plotWidth;
                var label = new TextBlock
                {
                    Text = $"{az}°",
                    Foreground = MediaBrushes.Gray,
                    FontSize = 10
                };
                Canvas.SetLeft(label, x - 15);
                Canvas.SetTop(label, centerY + 5);
                HorizonCanvas.Children.Add(label);
            }

            // Draw axes
            var xAxis = new Line
            {
                X1 = padding,
                Y1 = centerY,
                X2 = canvasWidth - padding,
                Y2 = centerY,
                    Stroke = MediaBrushes.Black,
                StrokeThickness = 2
            };
            HorizonCanvas.Children.Add(xAxis);

            var yAxis = new Line
            {
                X1 = padding,
                Y1 = padding,
                X2 = padding,
                Y2 = centerY,
                    Stroke = MediaBrushes.Black,
                StrokeThickness = 2
            };
            HorizonCanvas.Children.Add(yAxis);

            // Draw axis labels
            var xLabel = new TextBlock
            {
                Text = "Azimuth (degrees)",
                FontSize = 12,
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(xLabel, (canvasWidth - 100) / 2);
            Canvas.SetTop(xLabel, centerY + 20);
            HorizonCanvas.Children.Add(xLabel);

            var yLabel = new TextBlock
            {
                Text = "Max Altitude (degrees)",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                RenderTransform = new RotateTransform(-90)
            };
            Canvas.SetLeft(yLabel, 5);
            Canvas.SetTop(yLabel, (canvasHeight - 120) / 2);
            HorizonCanvas.Children.Add(yLabel);

            // Use the profile's GetMaxAltitude method to get interpolated values for all 360 degrees
            // This ensures we always have a complete horizon profile with proper interpolation
            var profile = new Models.HorizonProfile { Points = vm.HorizonPoints.ToList() };
            
            // Generate interpolated points for all 360 degrees
            var interpolatedPoints = new List<(int azimuth, double maxAltitude)>();
            for (int az = 0; az < 360; az++)
            {
                double maxAlt = profile.GetMaxAltitude(az);
                interpolatedPoints.Add((az, maxAlt));
            }

            // Create polygon for visible area (below the horizon line) - GREEN
            var visiblePath = new Path
            {
                Fill = new SolidColorBrush(MediaColor.FromArgb(128, 100, 255, 100)),
                Stroke = MediaBrushes.Green,
                StrokeThickness = 1
            };

            var visibleGeometry = new PathGeometry();
            var visibleFigure = new PathFigure();
            visibleFigure.StartPoint = new MediaPoint(padding, centerY);

            // Draw along the horizon line from left to right
            foreach (var point in interpolatedPoints)
            {
                var x = centerX + (point.azimuth / 360.0) * plotWidth;
                var y = centerY - (point.maxAltitude / maxAltitude) * plotHeight;
                visibleFigure.Segments.Add(new LineSegment(new MediaPoint(x, y), true));
            }

            // Close the path to bottom-right
            visibleFigure.Segments.Add(new LineSegment(new MediaPoint(canvasWidth - padding, centerY), true));
            visibleFigure.IsClosed = true;
            visibleGeometry.Figures.Add(visibleFigure);
            visiblePath.Data = visibleGeometry;
            HorizonCanvas.Children.Add(visiblePath);

            // Create polygon for blocked area (above the horizon line) - RED
            var blockedPath = new Path
            {
                Fill = new SolidColorBrush(MediaColor.FromArgb(128, 255, 100, 100)),
                Stroke = MediaBrushes.Red,
                StrokeThickness = 1
            };

            var blockedGeometry = new PathGeometry();
            var blockedFigure = new PathFigure();
            // Start from top-left
            blockedFigure.StartPoint = new MediaPoint(padding, padding);

            // Draw along top edge from left to right
            blockedFigure.Segments.Add(new LineSegment(new MediaPoint(canvasWidth - padding, padding), true));

            // Draw along top of horizon line (right to left, reversed)
            for (int i = interpolatedPoints.Count - 1; i >= 0; i--)
            {
                var point = interpolatedPoints[i];
                var x = centerX + (point.azimuth / 360.0) * plotWidth;
                var y = centerY - (point.maxAltitude / maxAltitude) * plotHeight;
                blockedFigure.Segments.Add(new LineSegment(new MediaPoint(x, y), true));
            }

            // Close the path back to top-left
            blockedFigure.IsClosed = true;
            blockedGeometry.Figures.Add(blockedFigure);
            blockedPath.Data = blockedGeometry;
            HorizonCanvas.Children.Add(blockedPath);

            // Create path for the horizon line
            var pathGeometry = new PathGeometry();
            var pathFigure = new PathFigure();
            var firstPoint = interpolatedPoints[0];
            var firstX = centerX + (firstPoint.azimuth / 360.0) * plotWidth;
            var firstY = centerY - (firstPoint.maxAltitude / maxAltitude) * plotHeight;
            pathFigure.StartPoint = new MediaPoint(firstX, firstY);

            foreach (var point in interpolatedPoints.Skip(1))
            {
                var x = centerX + (point.azimuth / 360.0) * plotWidth;
                var y = centerY - (point.maxAltitude / maxAltitude) * plotHeight;
                pathFigure.Segments.Add(new LineSegment(new MediaPoint(x, y), true));
            }

            pathGeometry.Figures.Add(pathFigure);
            var path = new Path
            {
                Data = pathGeometry,
                Stroke = MediaBrushes.Blue,
                StrokeThickness = 3
            };
            HorizonCanvas.Children.Add(path);

            // Draw points (only the actual user-defined points, not interpolated ones)
            var actualPoints = vm.HorizonPoints.OrderBy(p => p.Azimuth).ToList();
            foreach (var point in actualPoints)
            {
                var x = centerX + (point.Azimuth / 360.0) * plotWidth;
                var y = centerY - (point.MaxAltitude / maxAltitude) * plotHeight;
                var ellipse = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = MediaBrushes.Blue,
                    Stroke = MediaBrushes.DarkBlue,
                    StrokeThickness = 1
                };
                Canvas.SetLeft(ellipse, x - 4);
                Canvas.SetTop(ellipse, y - 4);
                HorizonCanvas.Children.Add(ellipse);
            }
        }

        private void CommitGrid(DataGrid? grid)
        {
            if (grid == null) return;
            try
            {
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch
            {
                // ignore commit issues
            }
        }

        private async void OnSaveProfileClick(object sender, RoutedEventArgs e)
        {
            CommitGrid(this.FindName("PointsGridManual") as DataGrid);
            CommitGrid(this.FindName("PointsGridCsv") as DataGrid);
            CommitGrid(this.FindName("PointsGridImage") as DataGrid);

            if (DataContext is MaximumHorizonOptionsViewModel vm && vm.SaveProfileCommand != null)
            {
                if (vm.SaveProfileCommand.CanExecute(null))
                {
                    vm.SaveProfileCommand.Execute(null);
                    // Wait a bit for the save to complete, then refresh visualization
                    await System.Threading.Tasks.Task.Delay(100);
                    UpdateHorizonVisualization();
                }
            }
        }
    }
}

