using System.Linq;
using Telerik.Windows.Controls;
using Telerik.Windows.DragDrop;
using Telerik.Windows.Controls.TreeView;

public partial class TreeViewPage : UserControl
{
    private readonly TreeViewPageViewModel _vm;

    public TreeViewPage()
    {
        InitializeComponent();

        _vm = (TreeViewPageViewModel)DataContext;

        // subscribe to drag-drop completed (note the 'true' for handled events)
        DragDropManager.AddDragDropCompletedHandler(Tree, OnTreeDragDropCompleted, true);
    }

    private async void OnTreeDragDropCompleted(object sender, DragDropCompletedEventArgs e)
    {
        // Extract Telerik's payload for tree drag/drop
        var options = DragDropPayloadManager
            .GetDataFromObject(e.Data, TreeViewDragDropOptions.Key) as TreeViewDragDropOptions;

        if (options == null) return;
        if (options.DropAction != DropAction.Move) return; // ignore copy/delete/etc.

        // We assume single-item drag
        var dragged = options.DraggedItems
                             .OfType<TreeNode>()
                             .FirstOrDefault();
        if (dragged == null) return;

        // New parent in the hierarchy (based on DropPosition)
        var dropTargetNode = options.DropTargetItem?.DataContext as TreeNode;
        TreeNode? newParent = null;

        switch (options.DropPosition)
        {
            case DropPosition.Inside:
                newParent = dropTargetNode;
                break;

            case DropPosition.Before:
            case DropPosition.After:
                // parent is the drop target's parent
                newParent = FindParentNode(dropTargetNode);
                break;
        }

        // At this point RadTreeView has already updated your collections,
        // so we can just use the current index within the parent's Children.
        var siblings = newParent == null
            ? _vm.RootNodes
            : newParent.Children;

        var newIndex = siblings.IndexOf(dragged);

        // Persist to DB (via your VM/repository)
        await _vm.SaveMoveAsync(dragged, newParent, newIndex);
    }

    private TreeNode? FindParentNode(TreeNode? child)
    {
        if (child == null) return null;

        // simplest: walk from RootNodes and find the parent
        foreach (var root in _vm.RootNodes)
        {
            var parent = FindParentRecursive(root, child);
            if (parent != null) return parent;
        }
        return null;
    }

    private TreeNode? FindParentRecursive(TreeNode current, TreeNode target)
    {
        if (current.Children.Contains(target))
            return current;

        foreach (var child in current.Children)
        {
            var result = FindParentRecursive(child, target);
            if (result != null) return result;
        }

        return null;
    }
}
