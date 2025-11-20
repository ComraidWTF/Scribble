private ScrollViewer? _scrollViewer;
private readonly double _scrollEdgeSize = 40;   // px from top/bottom to trigger scrolling
private readonly double _scrollSpeed = 1.5;     // adjust scroll speed

public MainWindow()
{
    InitializeComponent();
    TreeList.Loaded += (_, __) => _scrollViewer = GetScrollViewer(TreeList);
}

private ScrollViewer GetScrollViewer(DependencyObject parent)
{
    if (parent is ScrollViewer sv)
        return sv;

    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
    {
        var child = VisualTreeHelper.GetChild(parent, i);
        var result = GetScrollViewer(child);
        if (result != null)
            return result;
    }

    return null!;
}

private void TreeList_DragOver(object sender, DragEventArgs e)
{
    if (_scrollViewer == null)
        return;

    Point p = e.GetPosition(TreeList);
    double height = TreeList.ActualHeight;

    // Scroll UP
    if (p.Y < _scrollEdgeSize)
    {
        _scrollViewer.ScrollToVerticalOffset(_scrollViewer.VerticalOffset - _scrollSpeed);
    }

    // Scroll DOWN
    else if (p.Y > height - _scrollEdgeSize)
    {
        _scrollViewer.ScrollToVerticalOffset(_scrollViewer.VerticalOffset + _scrollSpeed);
    }
}
