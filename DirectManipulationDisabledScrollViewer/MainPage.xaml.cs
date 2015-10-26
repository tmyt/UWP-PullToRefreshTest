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

        // 引っ張って更新が有効？
        private bool isRefreshEnabled = true;
        // 更新処理中？
        private bool isRefreshing;
        // 慣性スクロールでの移動を無視する？
        private bool ignoreInertia;
        // 更新を実行する閾値
        private double threshold = 60.0;

        // Manipulationを開始したx, y 座標
        private double x, y;
        // 慣性スクロールで境界エフェクトを表示し始めた時刻
        private long inertiaStarted;

        private void ScrollViewer_OnManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            if (e.PointerDeviceType != PointerDeviceType.Touch) return;
            var sv = FindSV((DependencyObject)sender);
            // 境界エフェクトを出す距離を計算
            var overhangX = x - e.Cumulative.Translation.X;
            var overhangY = y - e.Cumulative.Translation.Y;
            // 更新中のヘッダオフセットを計算
            var refreshingOffset = isRefreshing ? 50 : 0;
            // ScrollViewerを正しい位置へスクロール
            sv.ChangeView(x - e.Cumulative.Translation.X, y - e.Cumulative.Translation.Y, null);
            // 境界エフェクトの計算をします。
            var tr = ((FrameworkElement)sender).RenderTransform as TranslateTransform;
            // スクロールが無効の時は処理しない
            if (sv.HorizontalScrollMode == ScrollMode.Disabled) { }
            // 左端よりさらにスクロールされた場合は、距離の1/4を境界エフェクトとして表示
            else if (overhangX < 0) { tr.X = (-overhangX) / 4; }
            // 右端よりさらにスクロールされた場合は、同様に距離の1/4を境界エフェクトとして表示
            else if (overhangX > sv.ScrollableWidth) { tr.X = (sv.ScrollableWidth - overhangX) / 4; }
            // どちらでもない場合はRenderTransformを初期化
            else { tr.X = 0; }
            // 水平スクロールと同じ感じ
            if (sv.VerticalScrollMode == ScrollMode.Disabled) { }　
            // 更新中のヘッダオフセットを考慮して境界エフェクトを表示
            else if (overhangY < 0) { tr.Y = refreshingOffset + (-overhangY) / 4; }
            // 下端の処理
            else if (overhangY > sv.ScrollableHeight) { tr.Y = (sv.ScrollableHeight - overhangY) / 4; }
            // どちらでもなく、更新処理中でなければRenderTransformを初期化
            else if (!isRefreshing) { tr.Y = 0; }
            // 引っ張って更新のインジケータを更新する
            var border = (Border)((Panel)((FrameworkElement)sender).Parent).FindName("RefreshBorder");
            if (!isRefreshing)
            {
                ((TranslateTransform)border.RenderTransform).Y = -50 + tr.Y;
                ((TextBlock)((StackPanel)border.Child).Children[0]).Text = (!ignoreInertia && tr.Y > threshold) ? "\uE149" : "\uE74B";
                ((TextBlock)((StackPanel)border.Child).Children[1]).Text = (!ignoreInertia && tr.Y > threshold) ? "Release to Refresh" : "Pull to Refresh";
            }
            // 慣性スクロール中で、境界エフェクトを表示すべき条件が整った
            if ((Math.Abs(tr.X) > 0 || tr.Y > refreshingOffset || tr.Y < 0) && e.IsInertial)
            {
                // 初回は時刻を記録
                if (inertiaStarted == 0)
                {
                    inertiaStarted = DateTime.UtcNow.Ticks;
                }
                // 慣性スクロールで境界エフェクトを300ms以上表示した場合Manipulationを終了
                if ((DateTime.UtcNow.Ticks - inertiaStarted) > 1000000) // 100ms
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
            // 閾値に達していない状態で慣性スクロールが始まった場合引っ張って更新の計算をスキップする
            ignoreInertia = tr.Y < threshold && !isRefreshing;
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
            // それなりにアニメーションさせる
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

            // メッセージを更新
            if (isRefreshing)
            {
                ((TextBlock)((StackPanel)border.Child).Children[0]).Text = "\uE149";
                ((TextBlock)((StackPanel)border.Child).Children[1]).Text = "Refreshing...";
            }
        }

        private async Task FireRefresh(object sender)
        {
            // なんかする。とりあえず3秒スリープ
            await Task.Delay(3000);
            CompleteRefresh(sender);
        }

        private void CompleteRefresh(object sender)
        {
            isRefreshing = false;
            // それなりなアニメーションを実行する
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
            // メッセージを更新
            ((TextBlock)((StackPanel)border.Child).Children[0]).Text = (!ignoreInertia && tr.Y > threshold) ? "\uE149" : "\uE74B";
            ((TextBlock)((StackPanel)border.Child).Children[1]).Text = (!ignoreInertia && tr.Y > threshold) ? "Release to Refresh" : "Pull to Refresh";
        }

        private void FrameworkElement_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            ((RectangleGeometry)((Grid)sender).Clip).Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
        }
    }
}
