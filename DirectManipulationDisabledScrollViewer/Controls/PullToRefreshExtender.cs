using System;
using System.Threading.Tasks;
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using DirectManipulationDisabledScrollViewer.Extensions;

namespace DirectManipulationDisabledScrollViewer.Controls
{
    public class PullToRefreshExtender : DependencyObject
    {
        public static readonly DependencyProperty IsRefreshEnabledProperty = DependencyProperty.Register(
            "IsRefreshEnabled", typeof(bool), typeof(PullToRefreshExtender), new PropertyMetadata(default(bool), OnIsRefreshEnabledChanged));

        public bool IsRefreshEnabled
        {
            get { return (bool)GetValue(IsRefreshEnabledProperty); }
            set { SetValue(IsRefreshEnabledProperty, value); }
        }

        private static void OnIsRefreshEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var i = (PullToRefreshExtender)d;
            if (i._indicator == null) return;
            i._indicator.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public PullToRefreshExtender()
        {
            IsRefreshEnabled = true;
        }

        // ターゲットScrollViewer
        private Grid _container;
        private ScrollViewer _scrollViewer;
        private ScrollContentPresenter _presenter;
        private PullToRefreshIndicator _indicator;

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

        private void ListView_Loaded(object sender, RoutedEventArgs e)
        {
            ((FrameworkElement)sender).Loaded -= ListView_Loaded;
            // try access scrollviewer
            var sv = ((DependencyObject)sender).FindFirstChild<ScrollViewer>();
            if (sv != null)
            {
                UpdateProperties(sv);
            }
        }

        private void ScrollContentPresenter_OnManipulationStarting(object sender, ManipulationStartingRoutedEventArgs e)
        {
            x = _scrollViewer.HorizontalOffset;
            y = _scrollViewer.VerticalOffset;
        }

        private void ScrollContentPresenter_OnManipulationInertiaStarting(object sender, ManipulationInertiaStartingRoutedEventArgs e)
        {
            var tr = _presenter.RenderTransform as TranslateTransform;
            // 閾値に達していない状態で慣性スクロールが始まった場合引っ張って更新の計算をスキップする
            ignoreInertia = tr.Y < threshold && !isRefreshing;
        }

        private void ScrollContentPresenter_OnManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            if (e.PointerDeviceType != PointerDeviceType.Touch) return;
            // 境界エフェクトを出す距離を計算
            var overhangX = x - e.Cumulative.Translation.X;
            var overhangY = y - e.Cumulative.Translation.Y;
            // 更新中のヘッダオフセットを計算
            var refreshingOffset = isRefreshing ? 50 : 0;
            // ScrollViewerを正しい位置へスクロール
            _scrollViewer.ChangeView(x - e.Cumulative.Translation.X, y - e.Cumulative.Translation.Y, null);
            // 境界エフェクトの計算をします。
            var tr = _presenter.RenderTransform as TranslateTransform;
            // スクロールが無効の時は処理しない
            if (_scrollViewer.HorizontalScrollMode == ScrollMode.Disabled) { }
            // 左端よりさらにスクロールされた場合は、距離の1/4を境界エフェクトとして表示
            else if (overhangX < 0) { tr.X = (-overhangX) / 4; }
            // 右端よりさらにスクロールされた場合は、同様に距離の1/4を境界エフェクトとして表示
            else if (overhangX > _scrollViewer.ScrollableWidth) { tr.X = (_scrollViewer.ScrollableWidth - overhangX) / 4; }
            // どちらでもない場合はRenderTransformを初期化
            else { tr.X = 0; }
            // 水平スクロールと同じ感じ
            if (_scrollViewer.VerticalScrollMode == ScrollMode.Disabled) { }
            // 更新中のヘッダオフセットを考慮して境界エフェクトを表示
            else if (overhangY < 0) { tr.Y = refreshingOffset + (-overhangY) / 4; }
            // 下端の処理
            else if (overhangY > _scrollViewer.ScrollableHeight) { tr.Y = (_scrollViewer.ScrollableHeight - overhangY) / 4; }
            // どちらでもなく、更新処理中でなければRenderTransformを初期化
            else if (!isRefreshing) { tr.Y = 0; }
            // 引っ張って更新のインジケータを更新する
            if (!isRefreshing)
            {
                ((TranslateTransform)_indicator.RenderTransform).Y = -50 + tr.Y;
                _indicator.Value = tr.Y / threshold;
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

        private void ScrollContentPresenter_OnManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            var tr = _presenter.RenderTransform as TranslateTransform;
            var btr = _indicator.RenderTransform as TranslateTransform;
            // check refresh
            if (IsRefreshEnabled && !isRefreshing && !ignoreInertia)
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
            Storyboard.SetTarget(bdranim, _indicator);
            Storyboard.SetTargetProperty(bdranim, "(UIElement.RenderTransform).(TranslateTransform.Y)");
            sb.Children.Add(xanim);
            sb.Children.Add(yanim);
            sb.Children.Add(bdranim);
            sb.Begin();

            // メッセージを更新
            if (isRefreshing)
            {
                _indicator.BeginRrefresh();
            }
        }

        private void Grid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _container.Clip.Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
        }

        private void CompleteRefresh(object sender)
        {
            isRefreshing = false;
            // それなりなアニメーションを実行する
            var tr = _presenter.RenderTransform as TranslateTransform;
            var btr = _indicator.RenderTransform as TranslateTransform;
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
            Storyboard.SetTarget(bdranim, _indicator);
            Storyboard.SetTargetProperty(bdranim, "(UIElement.RenderTransform).(TranslateTransform.Y)");
            sb.Children.Add(xanim);
            sb.Children.Add(yanim);
            sb.Children.Add(bdranim);
            sb.Begin();
            // メッセージを更新
            _indicator.EndRefresh();
        }

        private async Task FireRefresh(object sender)
        {
            // なんかする。とりあえず3秒スリープ
            await Task.Delay(3000);
            CompleteRefresh(sender);
        }

        private void UpdateProperties(ScrollViewer sv)
        {
            // find child
            _scrollViewer = sv;
            _presenter = _scrollViewer.FindFirstChild<ScrollContentPresenter>();
            _container = (Grid)VisualTreeHelper.GetParent(_presenter);

            // add indicator
            _indicator = new PullToRefreshIndicator();
            _indicator.Height = 50;
            _indicator.VerticalAlignment = VerticalAlignment.Top;
            _indicator.RenderTransform = new TranslateTransform { Y = -50 };
            _indicator.Visibility = IsRefreshEnabled ? Visibility.Visible : Visibility.Collapsed;
            _container.Children.Insert(0, _indicator);

            // update properties
            _container.SizeChanged += Grid_SizeChanged;
            _container.Clip = new RectangleGeometry { Rect = new Rect(0, 0, _container.ActualWidth, _container.ActualHeight) };
            _presenter.ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY |
                                          ManipulationModes.TranslateRailsX | ManipulationModes.TranslateRailsY |
                                          ManipulationModes.TranslateInertia;
            _presenter.ManipulationStarting += ScrollContentPresenter_OnManipulationStarting;
            _presenter.ManipulationInertiaStarting += ScrollContentPresenter_OnManipulationInertiaStarting;
            _presenter.ManipulationDelta += ScrollContentPresenter_OnManipulationDelta;
            _presenter.ManipulationCompleted += ScrollContentPresenter_OnManipulationCompleted;
            _presenter.RenderTransform = new TranslateTransform();
        }

        internal void Attach(DependencyObject element)
        {
            // try access scrollviewer
            var sv = element.FindFirstChild<ScrollViewer>();
            if (sv == null)
            {
                // if null, wait loaded
                ((FrameworkElement)element).Loaded += ListView_Loaded;
                return;
            }
            UpdateProperties(sv);
        }

        internal void Detach(DependencyObject element)
        {
            if (_indicator == null) return;

            // remove indicator
            _container.Children.Remove(_indicator);

            // restore properties
            _container.SizeChanged -= Grid_SizeChanged;
            _container.Clip = null;
            _presenter.ManipulationMode = ManipulationModes.System;
            _presenter.ManipulationStarting -= ScrollContentPresenter_OnManipulationStarting;
            _presenter.ManipulationInertiaStarting -= ScrollContentPresenter_OnManipulationInertiaStarting;
            _presenter.ManipulationDelta -= ScrollContentPresenter_OnManipulationDelta;
            _presenter.ManipulationCompleted -= ScrollContentPresenter_OnManipulationCompleted;
            _presenter.RenderTransform = null;
        }
    }
}
