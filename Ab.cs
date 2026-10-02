public static class RadComboBoxBehavior
{
    public static readonly DependencyProperty DisableHoverAutoScrollProperty =
        DependencyProperty.RegisterAttached(
            "DisableHoverAutoScroll",
            typeof(bool),
            typeof(RadComboBoxBehavior),
            new PropertyMetadata(false, OnChanged));

    public static void SetDisableHoverAutoScroll(
        DependencyObject obj,
        bool value)
    {
        obj.SetValue(DisableHoverAutoScrollProperty, value);
    }

    public static bool GetDisableHoverAutoScroll(
        DependencyObject obj)
    {
        return (bool)obj.GetValue(DisableHoverAutoScrollProperty);
    }

    private static void OnChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is not RadComboBox comboBox)
            return;

        if ((bool)e.NewValue)
            comboBox.AddHandler(
                FrameworkElement.RequestBringIntoViewEvent,
                new RequestBringIntoViewEventHandler(OnRequestBringIntoView));
        else
            comboBox.RemoveHandler(
                FrameworkElement.RequestBringIntoViewEvent,
                new RequestBringIntoViewEventHandler(OnRequestBringIntoView));
    }

    private static void OnRequestBringIntoView(
        object sender,
        RequestBringIntoViewEventArgs e)
    {
        if (e.OriginalSource is RadComboBoxItem)
        {
            e.Handled = true;
        }
    }
}
