private async void OnTreeDragDropCompleted(object sender, DragDropCompletedEventArgs e)
{
    var options = DragDropPayloadManager
        .GetDataFromObject(e.Data, TreeViewDragDropOptions.Key) as TreeViewDragDropOptions;

    if (options == null) return;
    if (options.DropAction != DropAction.Move) return;

    var dragged = options.DraggedItems.OfType<TreeNode>().FirstOrDefault();
    if (dragged == null) return;

    var dropTargetNode = options.DropTargetItem?.DataContext as TreeNode;

    // 1) Figure out the "target parent" and siblings collection
    TreeNode? newParent = null;
    ObservableCollection<TreeNode> siblings;

    if (options.DropPosition == DropPosition.Inside)
    {
        // Dropped ON a node -> dragged becomes its child
        newParent = dropTargetNode;
        siblings = newParent?.Children ?? _vm.RootNodes; // null-safety
    }
    else
    {
        // Dropped BEFORE/AFTER a node -> parent is that node's parent
        var parent = FindParentNode(dropTargetNode); // returns null if dropTarget is root
        newParent = parent;
        siblings = parent == null ? _vm.RootNodes : parent.Children;
    }

    // 2) Compute the index where the dragged node should end up
    int newIndex;

    if (options.DropPosition == DropPosition.Inside)
    {
        // Usually append at end of children for Inside
        newIndex = siblings.Count; 
    }
    else
    {
        var targetIndex = siblings.IndexOf(dropTargetNode!);

        newIndex = options.DropPosition switch
        {
            DropPosition.Before => targetIndex,
            DropPosition.After  => targetIndex + 1,
            _                   => targetIndex
        };
    }

    // 3) RadTreeView has already moved the item in the collection,
    //    but to be safe, ensure the siblings collection is correct
    //    and read the actual index from it:
    newIndex = siblings.IndexOf(dragged);

    // 4) Persist – this works for root and non-root the same way
    await _vm.SaveMoveAsync(dragged, newParent, newIndex);
}
