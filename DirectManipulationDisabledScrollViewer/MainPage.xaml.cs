using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace DirectManipulationDisabledScrollViewer
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();

            view.ItemsSource = Enumerable.Range(0, 1000);
        }

        private bool isRefreshEnabled = true;
        private bool isRefreshing;
        private bool ignoreInertia;
        private double threshold = 60.0;

        private double x;
        private double y;
        private long inertiaStarted;

        private void ScrollViewer_OnManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            if (e.PointerDeviceType != PointerDeviceType.Touch) return;
            var sv = FindSV((DependencyObject)sender);
            var overhangX = x - e.Cumulative.Translation.X;
            var overhangY = y - e.Cumulative.Translation.Y;
            var refreshingOffset = isRefreshing ? 50 : 0;
            sv.ChangeView(x - e.Cumulative.Translation.X, y - e.Cumulative.Translation.Y, null);
            var tr = ((FrameworkElement)sender).RenderTransform as TranslateTransform;
            if (sv.ComputedHorizontalScrollBarVisibility == Visibility.Collapsed) { }
            else if (overhangX < 0) { tr.X = (-overhangX) / 4; }
            else if (overhangX > sv.ScrollableWidth) { tr.X = (sv.ScrollableWidth - overhangX) / 4; }
            else { tr.X = 0; }
            if (sv.ComputedVerticalScrollBarVisibility == Visibility.Collapsed) { }　
            else if (overhangY < 0) { tr.Y = refreshingOffset + (-overhangY) / 4; }
            else if (overhangY > sv.ScrollableHeight) { tr.Y = (sv.ScrollableHeight - overhangY) / 4; }
            else if (!isRefreshing) { tr.Y = 0; }
            var border = (Border)((Panel)((FrameworkElement)sender).Parent).FindName("RefreshBorder");
            if (!isRefreshing)
            {
                ((TranslateTransform)border.RenderTransform).Y = -50 + tr.Y;
                ((TextBlock)((StackPanel)border.Child).Children[0]).Text = (!ignoreInertia && tr.Y > threshold) ? "\uE149" : "\uE74B";
                ((TextBlock)((StackPanel)border.Child).Children[1]).Text = (!ignoreInertia && tr.Y > threshold) ? "Release to Refresh" : "Pull to Refresh";
            }
            if ((Math.Abs(tr.X) > 0 || tr.Y > refreshingOffset || tr.Y < 0) && e.IsInertial)
            {
                if (inertiaStarted == 0)
                {
                    inertiaStarted = DateTime.UtcNow.Ticks;
                }
                if ((DateTime.UtcNow.Ticks - inertiaStarted) > 3000) // 300ms
                {
                    e.Complete();
                }
            }
        }

        ScrollViewer FindSV(DependencyObject d)
        {
            var e = d;
            while (e != null)
            {
                e = VisualTreeHelper.GetParent(e);
                if (e is ScrollViewer) return (ScrollViewer)e;
            }
            return null;
        }

        private void ScrollContentPresenter_OnManipulationStarting(object sender, ManipulationStartingRoutedEventArgs e)
        {
            var sv = FindSV((DependencyObject)sender);
            x = sv.HorizontalOffset;
            y = sv.VerticalOffset;
        }

        private void ScrollContentPresenter_OnManipulationInertiaStarting(object sender, ManipulationInertiaStartingRoutedEventArgs e)
        {
            var tr = ((FrameworkElement)sender).RenderTransform as TranslateTransform;
            ignoreInertia = tr.Y < threshold;
        }

        private void ScrollContentPresenter_OnManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            var tr = ((FrameworkElement)sender).RenderTransform as TranslateTransform;
            var border = (Border)((Panel)((FrameworkElement)sender).Parent).FindName("RefreshBorder");
            var btr = border.RenderTransform as TranslateTransform;
            // check refresh
            if (isRefreshEnabled && !isRefreshing && !ignoreInertia)
            {
                if (tr.Y > threshold)
                {
                    isRefreshing = true;
                    FireRefresh(sender);
                }
            }
            ignoreInertia = false;
            inertiaStarted = 0;
            var sb = new Storyboard();
            var xanim = new DoubleAnimation
            {
                From = tr.X,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
            };
            var yanim = new DoubleAnimation
            {
                From = tr.Y,
                To = isRefreshing ? 50 : 0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
            };
            var bdranim = new DoubleAnimation
            {
                From = btr.Y,
                To = isRefreshing ? 0 : -50,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(xanim, (DependencyObject)sender);
            Storyboard.SetTargetProperty(xanim, "(UIElement.RenderTransform).(TranslateTransform.X)");
            Storyboard.SetTarget(yanim, (DependencyObject)sender);
            Storyboard.SetTargetProperty(yanim, "(UIElement.RenderTransform).(TranslateTransform.Y)");
            Storyboard.SetTarget(bdranim, border);
            Storyboard.SetTargetProperty(bdranim, "(UIElement.RenderTransform).(TranslateTransform.Y)");
            sb.Children.Add(xanim);
            sb.Children.Add(yanim);
            sb.Children.Add(bdranim);
            sb.Begin();

            if (isRefreshing)
            {
                ((TextBlock)((StackPanel)border.Child).Children[0]).Text = "\uE149";
                ((TextBlock)((StackPanel)border.Child).Children[1]).Text = "Refreshing...";
            }
        }

        private async Task FireRefresh(object sender)
        {
            await Task.Delay(3000);
            CompleteRefresh(sender);
        }

        private void CompleteRefresh(object sender)
        {
            isRefreshing = false;
            var tr = ((FrameworkElement)sender).RenderTransform as TranslateTransform;
            var border = (Border)((Panel)((FrameworkElement)sender).Parent).FindName("RefreshBorder");
            var btr = border.RenderTransform as TranslateTransform;
            var sb = new Storyboard();
            var xanim = new DoubleAnimation
            {
                From = tr.X,
                To = isRefreshing ? 50 : 0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
            };
            var yanim = new DoubleAnimation
            {
                From = tr.Y,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
            };
            var bdranim = new DoubleAnimation
            {
                From = btr.Y,
                To = -50,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(xanim, (DependencyObject)sender);
            Storyboard.SetTargetProperty(xanim, "(UIElement.RenderTransform).(TranslateTransform.X)");
            Storyboard.SetTarget(yanim, (DependencyObject)sender);
            Storyboard.SetTargetProperty(yanim, "(UIElement.RenderTransform).(TranslateTransform.Y)");
            Storyboard.SetTarget(bdranim, border);
            Storyboard.SetTargetProperty(bdranim, "(UIElement.RenderTransform).(TranslateTransform.Y)");
            sb.Children.Add(xanim);
            sb.Children.Add(yanim);
            sb.Children.Add(bdranim);
            sb.Begin();
            ((TextBlock)((StackPanel)border.Child).Children[0]).Text = (!ignoreInertia && tr.Y > threshold) ? "\uE149" : "\uE74B";
            ((TextBlock)((StackPanel)border.Child).Children[1]).Text = (!ignoreInertia && tr.Y > threshold) ? "Release to Refresh" : "Pull to Refresh";
        }
    }
}
