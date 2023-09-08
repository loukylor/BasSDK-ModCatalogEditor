using System.Collections.Generic;
using System.Linq;
using System;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace ThunderRoad
{
    public class CatalogTreeView : TreeView
    {
        public Dictionary<string, CatalogData> openFiles;
        public Dictionary<int, TreeViewItem> items = new();

        public CatalogTreeView(Dictionary<string, CatalogData> openFiles, TreeViewState treeViewState) : base(treeViewState)
        {
            this.openFiles = openFiles;

            // Default foldout height is 8, so a 7 offset will make the height 15
            customFoldoutYOffset = 7;

            Reload();
        }

        protected override TreeViewItem BuildRoot()
        {
            TreeViewItem root = new(0, -1, "Root");
            Dictionary<Category, TreeViewItem> categories = new();
            int i = 0;
            foreach (Category category in Enum.GetValues(typeof(Category)).Cast<Category>().OrderBy(val => val.ToString()))
            {
                TreeViewItem item = new(++i, 0, category.ToString());
                items[i] = item;
                categories[category] = item;
                root.AddChild(item);
            }

            foreach (KeyValuePair<string, CatalogData> entry in openFiles)
            {
                Category dataCategory = Catalog.GetCategory(entry.Value.GetType());
                CatalogTreeViewItem item = new(++i, 2, entry);
                items[i] = item;
                categories[dataCategory].AddChild(item);
            }

            SetupDepthsFromParentsAndChildren(root);
            return root;
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            // Background color
            if (Event.current.type == EventType.Repaint && !args.selected && !args.isRenaming)
            {
                Rect rect = args.rowRect;
                rect.x = 0;
                if (args.item.depth == 0)
                {
                    DefaultStyles.backgroundOdd.Draw(rect, false, false, false, false);

                    // Bottom line color
                    rect.y += rect.height - 1;
                    rect.height = 1;
                    new GUIStyle(GUI.skin.horizontalScrollbar)
                    {
                        fixedHeight = 1
                    }.Draw(rect, false, false, false, false);
                }
                else
                    DefaultStyles.backgroundEven.Draw(rect, false, false, false, false);
            }

            // Render text
            args.rowRect.x = GetContentIndent(args.item) + GetFoldoutIndent(args.item);
            args.rowRect.height = GetCustomRowHeight(args.row, args.item);
            if (args.item is CatalogTreeViewItem item)
            {
                GUIContent idGUIContent = new(item.data.Value.id + (item.unsaved ? "*" : ""));
                GUI.Label(args.rowRect, idGUIContent);

                args.rowRect.x += GUI.skin.label.CalcSize(idGUIContent).x;
                GUIStyle italics = new(GUI.skin.label) 
                { 
                    fontStyle = FontStyle.Italic, 
                    normal = { textColor = Color.gray },
                    hover = { textColor = Color.gray }
                };
                GUI.Label(args.rowRect, new GUIContent($"({item.data.Key})"), italics);
            }
            else
                GUI.Label(args.rowRect, new GUIContent(args.label));
        }

        protected override float GetCustomRowHeight(int row, TreeViewItem item)
            => item.depth == 0 ? 30 : 20;

        protected override bool CanMultiSelect(TreeViewItem item)
            => false;

        public class CatalogTreeViewItem : TreeViewItem
        {
            public KeyValuePair<string, CatalogData> data;
            public bool unsaved = false;

            public CatalogTreeViewItem(int id, int depth, KeyValuePair<string, CatalogData> data) : base(id, depth) 
            {
                this.data = data;
            }
        }
    }
}