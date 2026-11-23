using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Couchmark.Extensions;

/// <summary>
/// Attached property to guarantee responsive, smooth mouse wheel scrolling
/// across all ScrollViewers and panels in the app, preventing nested elements or
/// custom controls from swallowing mouse wheel gestures.
/// </summary>
public static class ScrollViewerExtensions
{
    public static readonly DependencyProperty EnableMouseWheelScrollProperty =
        DependencyProperty.RegisterAttached(
            "EnableMouseWheelScroll",
            typeof(bool),
            typeof(ScrollViewerExtensions),
            new PropertyMetadata(false, OnEnableMouseWheelScrollChanged));

    public static bool GetEnableMouseWheelScroll(DependencyObject obj) =>
        (bool)obj.GetValue(EnableMouseWheelScrollProperty);

    public static void SetEnableMouseWheelScroll(DependencyObject obj, bool value) =>
        obj.SetValue(EnableMouseWheelScrollProperty, value);

    private static void OnEnableMouseWheelScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer scrollViewer)
        {
            scrollViewer.PreviewMouseWheel -= ScrollViewer_PreviewMouseWheel;
            if ((bool)e.NewValue)
            {
                if (scrollViewer.Background == null)
                {
                    scrollViewer.Background = Brushes.Transparent;
                }
                scrollViewer.PreviewMouseWheel += ScrollViewer_PreviewMouseWheel;
            }
        }
        else if (d is UIElement element)
        {
            element.PreviewMouseWheel -= Element_PreviewMouseWheel;
            if ((bool)e.NewValue)
            {
                element.PreviewMouseWheel += Element_PreviewMouseWheel;
            }
        }
    }

    private static void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not ScrollViewer scv) return;

        Scroll(scv, e);
    }

    private static void Element_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not DependencyObject dep) return;

        ScrollViewer? scv = FindScrollableAncestor(e.OriginalSource as DependencyObject) 
                            ?? FindScrollableDescendant(dep);

        if (scv != null)
        {
            Scroll(scv, e);
        }
    }

    private static void Scroll(ScrollViewer scv, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (scv.ScrollableWidth > 0)
            {
                double targetOffset = Math.Clamp(scv.HorizontalOffset - e.Delta, 0, scv.ScrollableWidth);
                if (Math.Abs(targetOffset - scv.HorizontalOffset) > 0.001)
                {
                    scv.ScrollToHorizontalOffset(targetOffset);
                    e.Handled = true;
                }
            }
        }
        else
        {
            if (scv.ScrollableHeight > 0)
            {
                double targetOffset = Math.Clamp(scv.VerticalOffset - e.Delta, 0, scv.ScrollableHeight);
                if (Math.Abs(targetOffset - scv.VerticalOffset) > 0.001)
                {
                    scv.ScrollToVerticalOffset(targetOffset);
                    e.Handled = true;
                }
            }
            else if (scv.ScrollableWidth > 0)
            {
                // Fallback: horizontal scroll when vertical scroll is not needed
                double targetOffset = Math.Clamp(scv.HorizontalOffset - e.Delta, 0, scv.ScrollableWidth);
                if (Math.Abs(targetOffset - scv.HorizontalOffset) > 0.001)
                {
                    scv.ScrollToHorizontalOffset(targetOffset);
                    e.Handled = true;
                }
            }
        }
    }

    private static ScrollViewer? FindScrollableAncestor(DependencyObject? start)
    {
        while (start != null)
        {
            if (start is ScrollViewer scv && (scv.ScrollableHeight > 0 || scv.ScrollableWidth > 0))
            {
                return scv;
            }
            start = VisualTreeHelper.GetParent(start);
        }
        return null;
    }

    private static ScrollViewer? FindScrollableDescendant(DependencyObject? root)
    {
        if (root == null) return null;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer scv && (scv.ScrollableHeight > 0 || scv.ScrollableWidth > 0))
            {
                return scv;
            }
            ScrollViewer? descendant = FindScrollableDescendant(child);
            if (descendant != null) return descendant;
        }
        return null;
    }
}
