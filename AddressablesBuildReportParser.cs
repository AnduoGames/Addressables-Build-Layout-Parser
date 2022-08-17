using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using System.Linq;
using UnityEditor.IMGUI.Controls;

public class AddressablesBuildReportParser : EditorWindow
{
    [MenuItem("Window/Asset Management")]
    public static void OnOpen()
    {
        var window = GetWindow<AddressablesBuildReportParser>();
        window.InitializeColumns();
        window.Show();
    }

    private int _selectedTab;
    private Vector2 _scroll;
    private string _paste;
    private bool _wasOnPaste;
    private int _selectedGroupIndex;
    private List<AddressableBundleGroupDataEntry> _assetGroups = new List<AddressableBundleGroupDataEntry>();

    private void OnGUI()
    {
        _selectedTab = GUILayout.Toolbar(_selectedTab, new string[] { "Paste", "Analyze" });

        if(_selectedTab == 0) DisplayPaste();
        else DisplayAnalyze();
    }

    private void DisplayPaste()
    {
        _paste = EditorGUILayout.TextArea(_paste);
        _wasOnPaste = true;
    }

    private void DisplayAnalyze()
    {
        if(_wasOnPaste)
        {
            ParsePaste();
            _wasOnPaste = false;
        }

        string[] options = _assetGroups.Select(x => x.Name).ToArray();
        _selectedGroupIndex = EditorGUILayout.Popup("Group", _selectedGroupIndex, options);

        ShowScrollview();
    }

    private void ParsePaste()
    {
        _assetGroups.Clear();

        var pasteLines = Regex.Split(_paste, "\r\n|\r|\n");

        var groups = Regex.Matches(_paste, @"Group (.+?) \(Bundles: (\d+), Total Size: ((?:\d+\.)*\d+)(.*B), Explicit Asset Count: (\d+)\)");

        Debug.Log("Found " + groups.Count + " groups");

        foreach (Match assetGroup in groups)
        {
            var name = assetGroup.Groups[1].Value;
            var bundlesCount = assetGroup.Groups[2].Value;
            var totalSize = assetGroup.Groups[3].Value;
            var totalSizeName = assetGroup.Groups[4].Value;
            var explicitAssetCount = assetGroup.Groups[5].Value;
            var line = LineFromPos(_paste, assetGroup.Index);

            var assetGroupEntry = new AddressableBundleGroupDataEntry
            {
                Name = name,
                BundlesCount = int.Parse(bundlesCount),
                Size = float.Parse(totalSize),
                SizeName = totalSizeName,
                ExplicitAssetCount = int.Parse(explicitAssetCount),
                LineInPaste = line
            };

            _assetGroups.Add(assetGroupEntry);
        }

        foreach (var assetGroup in _assetGroups)
        {
            var finalLine = -1;
            if (assetGroup != _assetGroups.Last())
                finalLine = _assetGroups.ElementAt(_assetGroups.IndexOf(assetGroup) + 1).LineInPaste;
            else
                finalLine = pasteLines.Count();

            var currentPaste = pasteLines.Skip(assetGroup.LineInPaste).Take(finalLine - assetGroup.LineInPaste)
                .Aggregate((x, y) => x + "\n" + y);

            var matches = Regex.Matches(currentPaste, @"\t\t\t(.+?) \(Size: ((?:\d+\.)*\d+)(.+?),");
            Debug.Log("Found " + matches.Count + " assets");
            foreach (Match match in matches)
            {
                var address = match.Groups[1].Value.Replace("\t", "");
                var size = match.Groups[2].Value;
                var sizeName = match.Groups[3].Value;

                assetGroup.DataEntries.Add(new AddressablesBuildReportDataEntry
                    { AssetAddress = address, Size = float.Parse(size), SizeName = sizeName });
            }

            assetGroup.DataEntries = assetGroup.DataEntries.OrderByDescending(x => x.ByteSize).ToList();
        }

        _selectedGroupIndex = _assetGroups.Count() - 1;
    }

    public int LineFromPos(string input, int indexPosition)
    {
        int lineNumber = 1;
        for (int i = 0; i < indexPosition; i++)
        {
            if (input[i] == '\n') lineNumber++;
        }
        return lineNumber;
    }

    private MultiColumnHeaderState _multiColumnHeaderState;
    private MultiColumnHeader _multiColumnHeader;

    private MultiColumnHeaderState.Column[] _columns;

    private void InitializeColumns()
    {
        // We can move these columns into some ScriptableObject or some other data saving object/file to save their properties there, otherwise because of some events these settings will be recreated and state of the window won't be saved as expected.
        _columns = new MultiColumnHeaderState.Column[]
        {
            new MultiColumnHeaderState.Column()
            {
                allowToggleVisibility = false, // At least one column must be there.
                autoResize = true,
                minWidth = 250.0f,
                width = 450f,
                canSort = true,
                sortingArrowAlignment = TextAlignment.Right,
                headerContent = new GUIContent("Path", "Path of the Asset."),
                headerTextAlignment = TextAlignment.Left,
            },
            new MultiColumnHeaderState.Column()
            {
                allowToggleVisibility = true,
                autoResize = true,
                minWidth = 50.0f,
                maxWidth = 100,
                canSort = true,
                sortingArrowAlignment = TextAlignment.Right,
                headerContent = new GUIContent("Size", "Size of the Asset."),
                headerTextAlignment = TextAlignment.Center,
            },
        };

        _multiColumnHeaderState = new MultiColumnHeaderState(columns: _columns);

        _multiColumnHeader = new MultiColumnHeader(state: _multiColumnHeaderState);

        // When we chagne visibility of the column we resize columns to fit in the window.
        _multiColumnHeader.visibleColumnsChanged += (multiColumnHeader) => multiColumnHeader.ResizeToFit();

        // Initial resizing of the content.
        _multiColumnHeader.ResizeToFit();

        _multiColumnHeader.sortingChanged += _multiColumnHeader_sortingChanged;
    }

    private void _multiColumnHeader_sortingChanged(MultiColumnHeader multiColumnHeader)
    {
        var entries = _assetGroups[_selectedGroupIndex].DataEntries;

        if (multiColumnHeader.sortedColumnIndex == 0)
        {
            if(multiColumnHeader.GetColumn(multiColumnHeader.sortedColumnIndex).sortedAscending)
                _assetGroups[_selectedGroupIndex].DataEntries = entries.OrderBy(x => x.AssetAddress).ToList();
            else
                _assetGroups[_selectedGroupIndex].DataEntries = entries.OrderByDescending(x => x.AssetAddress).ToList();
        }
        else
        {
            if (multiColumnHeader.GetColumn(multiColumnHeader.sortedColumnIndex).sortedAscending)
                _assetGroups[_selectedGroupIndex].DataEntries = entries.OrderBy(x => x.ByteSize).ToList();
            else
                _assetGroups[_selectedGroupIndex].DataEntries = entries.OrderByDescending(x => x.ByteSize).ToList();
        }
    }

    private readonly Color _lighterColor = Color.white * 0.3f;
    private readonly Color _darkerColor = Color.white * 0.1f;
    private int _selectedRow = -1;

    private void ShowScrollview()
    {
        // After compilation and some other events data of the window is lost if it's not saved in some kind of container. Usually those containers are ScriptableObject(s).
        if (_multiColumnHeader == null)
        {
            InitializeColumns();
        }

        var entries = _assetGroups[_selectedGroupIndex].DataEntries;

        // Basically we just draw something. Empty space. Which is `FlexibleSpace` here on top of the window.
        // We need this for - `GUILayoutUtility.GetLastRect()` because it needs at least 1 thing to be drawn before it.
        GUILayout.FlexibleSpace();

        // Get automatically aligned rect for our multi column header component.
        var windowRect = GUILayoutUtility.GetLastRect();

        // Here we are basically assigning the size of window to our newly positioned `windowRect`.
        windowRect.width = position.width;
        windowRect.height = position.height;

        var columnHeight = EditorGUIUtility.singleLineHeight;

        // This is a rect for our multi column table.
        var columnRectPrototype = new Rect(source: windowRect)
        {
            height = columnHeight, // This is basically a height of each column including header.
        };

        // Just enormously large view if you want it to span for the whole window. This is how it works [shrugs in confusion].
        var positionalRectAreaOfScrollView = GUILayoutUtility.GetRect(0, float.MaxValue, 0, float.MaxValue);

        // Create a `viewRect` since it should be separate from `rect` to avoid circular dependency.
        var viewRect = new Rect(source: windowRect)
        {
            xMax = _columns.Sum((column) => column.width), // Scroll max on X is basically a sum of width of columns.
            height = entries.Count * columnHeight
        };

        _scroll = GUI.BeginScrollView(
            position: positionalRectAreaOfScrollView,
            scrollPosition: _scroll,
            viewRect: viewRect,
            alwaysShowHorizontal: false,
            alwaysShowVertical: false
        );

        // Draw header for columns here.
        _multiColumnHeader.OnGUI(rect: columnRectPrototype, xScroll: 0.0f);

        for (int row = 0; row < entries.Count; row++)
        {
            var rowRect = new Rect(source: columnRectPrototype);

            rowRect.y += columnHeight * (row + 1);

            // Draw a texture before drawing each of the fields for the whole row.
            if(row == _selectedRow)
            {
                EditorGUI.DrawRect(rect: rowRect, color: new Color(37 / 255f, 53 / 255f, 138 / 255f));
            }
            else
            {
                if (row % 2 == 0)
                    EditorGUI.DrawRect(rect: rowRect, color: _darkerColor);
                else
                    EditorGUI.DrawRect(rect: rowRect, color: _lighterColor);
            }

            // Name field.
            int columnIndex = 0;
            GUIStyle nameFieldGUIStyle = new GUIStyle(GUI.skin.label)
            {
                padding = new RectOffset(left: 10, right: 10, top: 2, bottom: 2)
            };

            if (_multiColumnHeader.IsColumnVisible(columnIndex: columnIndex))
            {
                var visibleColumnIndex = _multiColumnHeader.GetVisibleColumnIndex(columnIndex: columnIndex);

                var columnRect = _multiColumnHeader.GetColumnRect(visibleColumnIndex: visibleColumnIndex);

                // This here basically is a row height, you can make it any value you like. Or you could calculate the max field height here that your object has and store it somewhere then use it here instead of `EditorGUIUtility.singleLineHeight`.
                // We move position of field on `y` by this height to get correct position.
                columnRect.y = rowRect.y;

                EditorGUI.LabelField(
                    position: _multiColumnHeader.GetCellRect(visibleColumnIndex: visibleColumnIndex, columnRect),
                    label: new GUIContent(entries[row].AssetAddress),
                    style: nameFieldGUIStyle
                );

                if (columnRect.Contains(Event.current.mousePosition) && Event.current.button == 0 && Event.current.isMouse)
                {
                    // do stuff
                    var asset = AssetDatabase.LoadAssetAtPath<Object>(entries[row].AssetAddress);
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                    _selectedRow = row;
                    Repaint();
                }
            }

            // Health slider field.
            columnIndex = 1;

            if (_multiColumnHeader.IsColumnVisible(columnIndex: columnIndex))
            {
                var visibleColumnIndex = _multiColumnHeader.GetVisibleColumnIndex(columnIndex: columnIndex);

                var columnRect = _multiColumnHeader.GetColumnRect(visibleColumnIndex: visibleColumnIndex);

                columnRect.y = rowRect.y;

                EditorGUI.LabelField(
                    position: _multiColumnHeader.GetCellRect(visibleColumnIndex: visibleColumnIndex, columnRect),
                    label: new GUIContent(entries[row].Size.ToString("0.00") + " " + entries[row].SizeName),
                    style: nameFieldGUIStyle
                );
            }
        }

        GUI.EndScrollView(handleScrollWheel: true);
    }
}

public class AddressableBundleGroupDataEntry
{
    public string Name;
    public int BundlesCount;
    public float Size;
    public string SizeName;
    public int ExplicitAssetCount;
    public int LineInPaste;

    public List<AddressablesBuildReportDataEntry> DataEntries = new List<AddressablesBuildReportDataEntry>();

    private readonly Dictionary<string, float> _sizeTable = new Dictionary<string, float>
    {
        { "B", 1 }, { "KB", 1000 }, { "MB", 1000 * 1000 }, { "GB", 1000 * 1000 * 1000 }
    };

    public float ByteSize => Size * _sizeTable[SizeName];
}

public class AddressablesBuildReportDataEntry
{
    public string AssetAddress;
    public float Size;
    public string SizeName;

    private readonly Dictionary<string, float> _sizeTable = new Dictionary<string, float>
    {
        { "B", 1 }, { "KB", 1000 }, { "MB", 1000 * 1000 }, { "GB", 1000 * 1000 * 1000 }
    };

    public float ByteSize => Size * _sizeTable[SizeName];
}
