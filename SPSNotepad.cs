using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace SPSNotepad
{
    class SingleInstanceController : Microsoft.VisualBasic.ApplicationServices.WindowsFormsApplicationBase
    {
        public SingleInstanceController()
        {
            this.IsSingleInstance = true;
            this.EnableVisualStyles = true;
            this.ShutdownStyle = Microsoft.VisualBasic.ApplicationServices.ShutdownMode.AfterAllFormsClose;
        }

        protected override void OnCreateMainForm()
        {
            this.MainForm = new MainForm(false);
            if (this.CommandLineArgs.Count > 0 && System.IO.File.Exists(this.CommandLineArgs[0]))
            {
                ((MainForm)this.MainForm).OpenFileFromPath(this.CommandLineArgs[0]);
            }
        }

        protected override void OnStartupNextInstance(Microsoft.VisualBasic.ApplicationServices.StartupNextInstanceEventArgs eventArgs)
        {
            if (eventArgs.CommandLine.Count > 0 && System.IO.File.Exists(eventArgs.CommandLine[0]))
            {
                if (SPSNotepad.MainForm.OpenForms.Count > 0)
                {
                    SPSNotepad.MainForm.OpenForms[0].OpenFileFromPath(eventArgs.CommandLine[0]);
                    SPSNotepad.MainForm.OpenForms[0].ForceToForeground();
                }
            }
            else
            {
                if (SPSNotepad.MainForm.OpenForms.Count > 0)
                {
                    SPSNotepad.MainForm.OpenForms[0].ForceToForeground();
                }
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            SingleInstanceController controller = new SingleInstanceController();
            controller.Run(args);
        }
    }

    public class MainForm : Form
    {
        public static List<MainForm> OpenForms = new List<MainForm>();
        public bool IsClosing = false;

        private MenuStrip menuStrip;
        private TabControl mainTabControl;
        private float currentZoom = 9f;

        // Find Bar
        private Panel findPanel;
        private TextBox txtFind;
        private Label lblFindCount;
        private Button btnFindNext, btnFindPrev, btnFindClose;
        private List<MatchResult> currentMatches = new List<MatchResult>();
        private int currentMatchIndex = -1;

        private TabPage draggedTab;

        private class MatchResult
        {
            public TextBoxBase Control;
            public int StartIndex;
            public int Length;
            public Control TileWrapper;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        public void ForceToForeground()
        {
            if (this.WindowState == FormWindowState.Minimized)
                this.WindowState = FormWindowState.Normal;

            this.TopMost = true;
            this.BringToFront();
            this.Activate();
            this.TopMost = false;
            
            SetForegroundWindow(this.Handle);
            this.Focus();
        }

        public MainForm(bool isTearOff)
        {
            OpenForms.Add(this);
            InitializeComponents(isTearOff);
        }

        private void InitializeComponents(bool isTearOff)
        {
            this.Text = "SPS Notepad";
            this.Size = new Size(300, 400); 
            this.MinimumSize = new Size(180, 200); 
            this.Font = new Font("Segoe UI", currentZoom);
            
            try { this.Icon = new Icon(System.IO.Path.Combine(Application.StartupPath, "logo.ico")); } catch { }

            menuStrip = new MenuStrip();
            var fileMenu = new ToolStripMenuItem("File");
            
            var openMenu = new ToolStripMenuItem("Open File... (Ctrl+O)", null, OpenFile);
            var newNotepadMenu = new ToolStripMenuItem("New Notepad Tab", null, (s, e) => AddNotepadTab("Notepad"));
            var newTrackerMenu = new ToolStripMenuItem("New Tracker Tab", null, (s, e) => AddTrackerTab("Tracker"));
            var saveMenu = new ToolStripMenuItem("Save Current Tab Data (Ctrl+S)", null, SaveCurrentTabData);
            
            fileMenu.DropDownItems.Add(openMenu);
            fileMenu.DropDownItems.Add(newNotepadMenu);
            fileMenu.DropDownItems.Add(newTrackerMenu);
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add(saveMenu);
            
            var viewMenu = new ToolStripMenuItem("View");
            viewMenu.DropDownItems.Add(new ToolStripMenuItem("Zoom In (Ctrl++)", null, (s, e) => Zoom(1)));
            viewMenu.DropDownItems.Add(new ToolStripMenuItem("Zoom Out (Ctrl+-)", null, (s, e) => Zoom(-1)));
            viewMenu.DropDownItems.Add(new ToolStripMenuItem("Reset Zoom", null, (s, e) => Zoom(0)));
            
            menuStrip.Items.Add(fileMenu);
            menuStrip.Items.Add(viewMenu);
            this.MainMenuStrip = menuStrip;
            this.Controls.Add(menuStrip);

            InitializeFindPanel();

            mainTabControl = new TabControl { Dock = DockStyle.Fill, AllowDrop = true };
            mainTabControl.MouseDown += mainTabControl_MouseDown;
            mainTabControl.MouseMove += mainTabControl_MouseMove;
            mainTabControl.DragEnter += mainTabControl_DragEnter;
            mainTabControl.DragDrop += mainTabControl_DragDrop;

            this.Controls.Add(mainTabControl);
            mainTabControl.BringToFront(); 
            
            mainTabControl.SelectedIndexChanged += (s, e) => {
                if (findPanel.Visible) PerformSearch();
            };

            if (!isTearOff)
            {
                LoadCache(this);
            }
        }

        private void mainTabControl_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                for (int i = 0; i < mainTabControl.TabPages.Count; i++)
                {
                    if (mainTabControl.GetTabRect(i).Contains(e.Location))
                    {
                        draggedTab = mainTabControl.TabPages[i];
                        break;
                    }
                }
            }
        }

        private void mainTabControl_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && draggedTab != null)
            {
                var result = mainTabControl.DoDragDrop(draggedTab, DragDropEffects.Move);
                if (result == DragDropEffects.None)
                {
                    Point p = this.PointToClient(Cursor.Position);
                    if (!this.ClientRectangle.Contains(p))
                    {
                        TearOffTab(draggedTab, Cursor.Position);
                    }
                }
                draggedTab = null;
            }
        }

        private void mainTabControl_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(TabPage)))
            {
                e.Effect = DragDropEffects.Move;
            }
        }

        private void mainTabControl_DragDrop(object sender, DragEventArgs e)
        {
            TabPage tab = (TabPage)e.Data.GetData(typeof(TabPage));
            if (tab != null && tab.Parent != mainTabControl)
            {
                var oldParent = (TabControl)tab.Parent;
                oldParent.TabPages.Remove(tab);
                mainTabControl.TabPages.Add(tab);
                mainTabControl.SelectedTab = tab;
                
                MainForm f = oldParent.FindForm() as MainForm;
                if (oldParent.TabPages.Count == 0 && f != null && f != this)
                {
                    f.Close();
                }
            }
        }

        private void TearOffTab(TabPage tab, Point screenLocation)
        {
            var oldParent = (TabControl)tab.Parent;
            if (oldParent.TabPages.Count <= 1) return; // Don't tear off if it's the last tab

            oldParent.TabPages.Remove(tab);
            
            MainForm newForm = new MainForm(true);
            newForm.StartPosition = FormStartPosition.Manual;
            newForm.Location = screenLocation;
            
            newForm.mainTabControl.TabPages.Clear();
            newForm.mainTabControl.TabPages.Add(tab);
            newForm.Show();
        }

        private void InitializeFindPanel()
        {
            findPanel = new Panel { Dock = DockStyle.Bottom, Height = 35, Visible = false, BackColor = SystemColors.ControlLight };
            txtFind = new TextBox { Left = 10, Top = 7, Width = 120 };
            lblFindCount = new Label { Left = 135, Top = 10, AutoSize = true, Text = "0/0" };
            btnFindPrev = new Button { Left = 180, Top = 5, Width = 30, Text = "<" };
            btnFindNext = new Button { Left = 215, Top = 5, Width = 30, Text = ">" };
            btnFindClose = new Button { Left = 250, Top = 5, Width = 30, Text = "X" };

            txtFind.TextChanged += (s, e) => PerformSearch();
            btnFindNext.Click += (s, e) => GoToMatch(1);
            btnFindPrev.Click += (s, e) => GoToMatch(-1);
            btnFindClose.Click += (s, e) => {
                findPanel.Visible = false;
                foreach (var m in currentMatches) m.Control.SelectionLength = 0;
                var currentTab = mainTabControl.SelectedTab;
                if (currentTab != null && currentTab.Controls.Count > 0)
                    currentTab.Controls[0].Focus();
            };

            findPanel.Controls.AddRange(new Control[] { txtFind, lblFindCount, btnFindPrev, btnFindNext, btnFindClose });
            this.Controls.Add(findPanel);
        }

        private void PerformSearch()
        {
            currentMatches.Clear();
            currentMatchIndex = -1;
            string query = txtFind.Text;
            if (string.IsNullOrEmpty(query)) 
            {
                lblFindCount.Text = "0/0";
                return;
            }

            var currentTab = mainTabControl.SelectedTab;
            if (currentTab == null || currentTab.Controls.Count == 0) return;

            TrackerContainer container = currentTab.Controls[0] as TrackerContainer;
            RichTextBox notepad = currentTab.Controls[0] as RichTextBox;

            if (container != null)
            {
                foreach (var tile in container.Tiles)
                {
                    foreach (var txt in tile.GetInputs())
                    {
                        if (txt.Visible)
                        {
                            int idx = 0;
                            while ((idx = txt.Text.IndexOf(query, idx, StringComparison.OrdinalIgnoreCase)) != -1)
                            {
                                currentMatches.Add(new MatchResult { Control = txt, StartIndex = idx, Length = query.Length, TileWrapper = tile.Parent });
                                idx += query.Length;
                            }
                        }
                    }
                }
            }
            else if (notepad != null)
            {
                int idx = 0;
                while ((idx = notepad.Text.IndexOf(query, idx, StringComparison.OrdinalIgnoreCase)) != -1)
                {
                    currentMatches.Add(new MatchResult { Control = notepad, StartIndex = idx, Length = query.Length });
                    idx += query.Length;
                }
            }

            if (currentMatches.Count > 0)
            {
                currentMatchIndex = 0;
                GoToMatch(0);
            }
            else
            {
                lblFindCount.Text = "0/0";
            }
        }

        private void GoToMatch(int direction)
        {
            if (currentMatches.Count == 0) return;

            foreach (var m in currentMatches)
            {
                m.Control.SelectionLength = 0;
            }

            currentMatchIndex += direction;
            if (currentMatchIndex >= currentMatches.Count) currentMatchIndex = 0;
            if (currentMatchIndex < 0) currentMatchIndex = currentMatches.Count - 1;

            lblFindCount.Text = string.Format("{0}/{1}", currentMatchIndex + 1, currentMatches.Count);

            var match = currentMatches[currentMatchIndex];
            match.Control.Select(match.StartIndex, match.Length);
            match.Control.ScrollToCaret();

            TrackerContainer c = match.TileWrapper != null ? match.TileWrapper.Parent as TrackerContainer : null;
            if (c != null)
            {
                c.ScrollControlIntoView(match.TileWrapper);
            }
        }
        
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (OpenForms.Count > 1 && mainTabControl.TabPages.Count > 0)
            {
                while (mainTabControl.TabPages.Count > 0)
                {
                    var page = mainTabControl.TabPages[mainTabControl.TabPages.Count - 1];
                    mainTabControl.SelectedTab = page;

                    bool isDirty = false;
                    if (page.Tag != null) isDirty = page.Text.EndsWith("*");
                    else isDirty = !IsTabEmpty(page);

                    if (!isDirty)
                    {
                        mainTabControl.TabPages.Remove(page);
                        continue;
                    }

                    string cleanName = page.Text.TrimEnd('*');
                    var result = CenteredMessageBox.Show(this, "Save changes to '" + cleanName + "' before closing?", "Close Window", MessageBoxButtons.YesNoCancel);
                    
                    if (result == DialogResult.Cancel)
                    {
                        e.Cancel = true;
                        return;
                    }
                    else if (result == DialogResult.No)
                    {
                        mainTabControl.TabPages.Remove(page);
                    }
                    else if (result == DialogResult.Yes)
                    {
                        if (SaveSpecificTab(page))
                        {
                            mainTabControl.TabPages.Remove(page);
                        }
                        else
                        {
                            e.Cancel = true;
                            return;
                        }
                    }
                }
            }

            IsClosing = true;
            base.OnFormClosing(e);

            if (!e.Cancel)
            {
                try { SaveCache(); } catch { }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            OpenForms.Remove(this);
            base.OnFormClosed(e);
            
            if (OpenForms.Count == 0)
            {
                Application.Exit();
            }
        }

        private bool IsTabEmpty(TabPage page)
        {
            if (page.Controls.Count == 0) return true;
            
            TrackerContainer c = page.Controls[0] as TrackerContainer;
            if (c != null)
            {
                if (c.Tiles.Count > 1) return false;
                if (c.Tiles.Count == 1)
                {
                    foreach (var txt in c.Tiles[0].GetInputs())
                    {
                        if (!string.IsNullOrEmpty(txt.Text)) return false;
                    }
                }
                return true;
            }
            
            RichTextBox tb = page.Controls[0] as RichTextBox;
            if (tb != null)
            {
                return string.IsNullOrEmpty(tb.Text);
            }
            return true;
        }

        private void SaveTrackerToFile(TrackerContainer container, string filePath)
        {
            using (StreamWriter sw = new StreamWriter(filePath))
            {
                foreach (var tile in container.Tiles)
                {
                    sw.WriteLine(tile.GetDataString());
                    sw.WriteLine(new string('-', 20));
                }
            }
        }

        private static void SaveCache()
        {
            XmlDocument doc = new XmlDocument();
            var root = doc.CreateElement("SPSNotepadCache");
            doc.AppendChild(root);

            foreach (var form in OpenForms)
            {
                // Skip the form if it's closing and there are other forms still open
                // (This ensures secondary windows are deleted from cache, but the final window is saved)
                if (form.IsClosing && OpenForms.Count > 1) continue;

                var windowNode = doc.CreateElement("Window");
                
                foreach (TabPage page in form.mainTabControl.TabPages)
                {
                    if (page.Controls.Count > 0)
                    {
                        TrackerContainer container = page.Controls[0] as TrackerContainer;
                        RichTextBox tb = page.Controls[0] as RichTextBox;

                        if (container != null)
                        {
                            var tabNode = doc.CreateElement("Tracker");
                            tabNode.SetAttribute("title", page.Text);
                            if (page.Tag != null) tabNode.SetAttribute("filePath", page.Tag.ToString());
                            
                            foreach (var tile in container.Tiles)
                            {
                                var tileNode = doc.CreateElement("Tile");
                                tileNode.SetAttribute("visnLabel", tile.GetVisnLabel());
                                tileNode.SetAttribute("phoneVisible", tile.IsPhoneVisible().ToString());
                                tileNode.SetAttribute("emailVisible", tile.IsEmailVisible().ToString());
                                
                                var inputs = tile.GetInputs();
                                for (int i = 0; i < inputs.Count; i++)
                                {
                                    var fieldNode = doc.CreateElement("F" + i);
                                    fieldNode.InnerText = inputs[i].Text;
                                    tileNode.AppendChild(fieldNode);
                                }
                                tabNode.AppendChild(tileNode);
                            }
                            windowNode.AppendChild(tabNode);
                        }
                        else if (tb != null)
                        {
                            var tabNode = doc.CreateElement("Notepad");
                            tabNode.SetAttribute("title", page.Text);
                            if (page.Tag != null) tabNode.SetAttribute("filePath", page.Tag.ToString());
                            var textNode = doc.CreateElement("Text");
                            textNode.InnerText = tb.Text;
                            tabNode.AppendChild(textNode);
                            windowNode.AppendChild(tabNode);
                        }
                    }
                }
                
                if (windowNode.ChildNodes.Count > 0)
                {
                    root.AppendChild(windowNode);
                }
            }
            
            doc.Save(System.IO.Path.Combine(Application.StartupPath, "cache.xml"));
        }

        private static void LoadCache(MainForm form)
        {
            string cachePath = System.IO.Path.Combine(Application.StartupPath, "cache.xml");
            if (!File.Exists(cachePath))
            {
                form.AddTrackerTab("Tracker");
                return;
            }

            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(cachePath);
                var root = doc.DocumentElement;
                
                XmlNodeList windowNodes = root.SelectNodes("Window");
                
                if (windowNodes.Count == 0)
                {
                    form.AddTrackerTab("Tracker");
                    return;
                }

                PopulateTabsFromNode(form, windowNodes[0]);

                for (int i = 1; i < windowNodes.Count; i++)
                {
                    MainForm newForm = new MainForm(true);
                    PopulateTabsFromNode(newForm, windowNodes[i]);
                    newForm.Show();
                }
            }
            catch
            {
                form.mainTabControl.TabPages.Clear();
                form.AddTrackerTab("Tracker");
            }
        }

        private static void PopulateTabsFromNode(MainForm form, XmlNode windowNode)
        {
            bool loadedAny = false;
            foreach (XmlNode node in windowNode.ChildNodes)
            {
                if (node.Name == "Notepad")
                {
                    string title = node.Attributes["title"] != null ? node.Attributes["title"].Value : "Notepad";
                    var tb = form.AddNotepadTab(title);
                    if (node.Attributes["filePath"] != null) tb.Parent.Tag = node.Attributes["filePath"].Value;
                    var textNode = node.SelectSingleNode("Text");
                    if (textNode != null) tb.Text = textNode.InnerText;
                    loadedAny = true;
                }
                else if (node.Name == "Tracker")
                {
                    string title = node.Attributes["title"] != null ? node.Attributes["title"].Value : "Tracker";
                    var container = form.AddTrackerTab(title);
                    if (node.Attributes["filePath"] != null) container.Parent.Tag = node.Attributes["filePath"].Value;
                    
                    container.Tiles.Clear();
                    container.Controls.Clear();
                    
                    foreach (XmlNode tileNode in node.SelectNodes("Tile"))
                    {
                        var tileWrapper = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 8) };
                        var tile = new TrackerTile(container) { Dock = DockStyle.Fill };
                        
                        string visnLbl = tileNode.Attributes["visnLabel"] != null ? tileNode.Attributes["visnLabel"].Value : "VisN";
                        tile.SetVisnLabel(visnLbl);
                        
                        bool phoneV = false;
                        if (tileNode.Attributes["phoneVisible"] != null && bool.TryParse(tileNode.Attributes["phoneVisible"].Value, out phoneV))
                            tile.SetPhoneVisible(phoneV);
                            
                        bool emailV = false;
                        if (tileNode.Attributes["emailVisible"] != null && bool.TryParse(tileNode.Attributes["emailVisible"].Value, out emailV))
                            tile.SetEmailVisible(emailV);

                        var inputs = tile.GetInputs();
                        for (int i = 0; i < inputs.Count; i++)
                        {
                            var fNode = tileNode.SelectSingleNode("F" + i);
                            if (fNode != null) inputs[i].Text = fNode.InnerText;
                        }

                        tileWrapper.Controls.Add(tile);
                        container.Controls.Add(tileWrapper);
                        tileWrapper.BringToFront();
                        container.Tiles.Add(tile);
                    }
                    
                    if (container.Tiles.Count == 0) container.AddNewTile();
                    loadedAny = true;
                }
            }
            
            if (!loadedAny) form.AddTrackerTab("Tracker");

            foreach (TabPage p in form.mainTabControl.TabPages)
            {
                p.Text = p.Text.TrimEnd('*');
            }
        }

        private void Zoom(int direction)
        {
            if (direction == 0) currentZoom = 9f;
            else currentZoom += direction;
            
            if (currentZoom < 4) currentZoom = 4;
            if (currentZoom > 30) currentZoom = 30;
            
            ApplyZoom(this, currentZoom);
        }

        private void ApplyZoom(Control ctrl, float size)
        {
            ctrl.Font = new Font(ctrl.Font.FontFamily, size, ctrl.Font.Style);
            foreach (Control child in ctrl.Controls)
            {
                ApplyZoom(child, size);
            }
        }

        private RichTextBox AddNotepadTab(string title)
        {
            var tab = new TabPage(title);
            var txt = new RichTextBox { 
                Dock = DockStyle.Fill, 
                ScrollBars = RichTextBoxScrollBars.Both, 
                Font = new Font("Consolas", currentZoom),
                HideSelection = false,
                DetectUrls = false
            };
            txt.TextChanged += (s, e) => { if (!tab.Text.EndsWith("*")) tab.Text += "*"; };
            tab.Controls.Add(txt);
            mainTabControl.TabPages.Add(tab);
            mainTabControl.SelectedTab = tab;
            return txt;
        }

        private TrackerContainer AddTrackerTab(string title)
        {
            var tab = new TabPage(title);
            var trackerContainer = new TrackerContainer(this) { Dock = DockStyle.Fill };
            tab.Controls.Add(trackerContainer);
            mainTabControl.TabPages.Add(tab);
            mainTabControl.SelectedTab = tab;
            ApplyZoom(tab, currentZoom);
            return trackerContainer;
        }

        private void SaveCurrentTabData(object sender, EventArgs e)
        {
            var currentTab = mainTabControl.SelectedTab;
            if (currentTab != null) SaveSpecificTab(currentTab);
        }

        private bool SaveSpecificTab(TabPage page)
        {
            if (page.Controls.Count == 0) return false;

            TrackerContainer container = page.Controls[0] as TrackerContainer;
            RichTextBox notepad = page.Controls[0] as RichTextBox;

            string filePath = page.Tag as string;
            bool hasPath = !string.IsNullOrEmpty(filePath);

            if (container != null)
            {
                if (hasPath)
                {
                    SaveTrackerToFile(container, filePath);
                    page.Text = page.Text.TrimEnd('*');
                    return true;
                }
                else
                {
                    string defaultName = page.Text;
                    if (!defaultName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) defaultName += ".txt";
                    
                    using (SaveFileDialog sfd = new SaveFileDialog() { Filter = "Text Files|*.txt", Title = "Save Tracker Data", FileName = defaultName })
                    {
                        if (sfd.ShowDialog() == DialogResult.OK)
                        {
                            SaveTrackerToFile(container, sfd.FileName);
                            page.Tag = sfd.FileName;
                            page.Text = Path.GetFileName(sfd.FileName);
                            return true;
                        }
                    }
                }
            }
            else if (notepad != null)
            {
                if (hasPath)
                {
                    File.WriteAllText(filePath, notepad.Text);
                    page.Text = page.Text.TrimEnd('*');
                    return true;
                }
                else
                {
                    string defaultName = page.Text;
                    if (!defaultName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) defaultName += ".txt";

                    using (SaveFileDialog sfd = new SaveFileDialog() { Filter = "Text Files|*.txt", Title = "Save Notepad Data", FileName = defaultName })
                    {
                        if (sfd.ShowDialog() == DialogResult.OK)
                        {
                            File.WriteAllText(sfd.FileName, notepad.Text);
                            page.Tag = sfd.FileName;
                            page.Text = Path.GetFileName(sfd.FileName);
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private bool ParseFileIntoTabs(string filePath)
        {
            foreach (MainForm form in OpenForms)
            {
                foreach (TabPage page in form.mainTabControl.TabPages)
                {
                    if (page.Tag != null && string.Equals(page.Tag.ToString(), filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        form.ForceToForeground();
                        form.mainTabControl.SelectedTab = page;
                        return false;
                    }
                }
            }

            string[] lines = File.ReadAllLines(filePath);
            
            if (lines.Length > 0 && lines[0].StartsWith("=== TAB: "))
            {
                List<string> currentTabLines = new List<string>();
                string currentTabTitle = "";
                
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("=== TAB: "))
                    {
                        if (currentTabTitle != "")
                        {
                            if (currentTabLines.Count > 0 && currentTabLines[0].StartsWith("Tag#:")) CommitParsedTab(currentTabTitle, "Tracker", currentTabLines);
                            else CommitParsedTab(currentTabTitle, "Notepad", currentTabLines);
                        }
                        currentTabTitle = lines[i].Replace("=== TAB: ", "").Replace(" ===", "").Trim();
                        currentTabLines.Clear();
                    }
                    else
                    {
                        currentTabLines.Add(lines[i]);
                    }
                }
                if (currentTabTitle != "")
                {
                    if (currentTabLines.Count > 0 && currentTabLines[0].StartsWith("Tag#:")) CommitParsedTab(currentTabTitle, "Tracker", currentTabLines);
                    else CommitParsedTab(currentTabTitle, "Notepad", currentTabLines);
                }
            }
            else
            {
                if (lines.Length > 0 && lines[0].StartsWith("Tag#:")) CommitParsedTab(Path.GetFileName(filePath), "Tracker", new List<string>(lines), filePath);
                else CommitParsedTab(Path.GetFileName(filePath), "Notepad", new List<string>(lines), filePath);
            }
            return true;
        }

        private void OpenFile(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog() { Filter = "Text Files|*.txt", Title = "Open File" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    ParseFileIntoTabs(ofd.FileName);
                }
            }
        }

        public void OpenFileFromPath(string filePath)
        {
            try
            {
                if (ParseFileIntoTabs(filePath))
                {
                    if (mainTabControl.TabPages.Count > 1 && mainTabControl.TabPages[0].Text == "Tracker" && IsTabEmpty(mainTabControl.TabPages[0]) && mainTabControl.TabPages[0].Tag == null)
                    {
                        mainTabControl.TabPages.RemoveAt(0);
                    }
                    
                    mainTabControl.SelectedTab = mainTabControl.TabPages[mainTabControl.TabPages.Count - 1];
                }
            }
            catch (Exception ex)
            {
                CenteredMessageBox.Show(this, "Error opening file: " + ex.Message, "Error", MessageBoxButtons.OK);
            }
        }

        private void CommitParsedTab(string title, string type, List<string> lines, string filePath = null)
        {
            TabPage newTab = null;
            if (type == "Tracker")
            {
                var container = AddTrackerTab(title);
                newTab = (TabPage)container.Parent;
                container.Tiles.Clear();
                container.Controls.Clear();
                
                List<string> tileLines = new List<string>();
                foreach (string line in lines)
                {
                    if (line.StartsWith("--------------------"))
                    {
                        if (tileLines.Count > 0) ParseTile(container, tileLines);
                        tileLines.Clear();
                    }
                    else if (!string.IsNullOrWhiteSpace(line))
                    {
                        tileLines.Add(line);
                    }
                }
                if (tileLines.Count > 0) ParseTile(container, tileLines);
                
                if (container.Tiles.Count == 0) container.AddNewTile();
            }
            else
            {
                var tb = AddNotepadTab(title);
                newTab = (TabPage)tb.Parent;
                tb.Text = string.Join("\r\n", lines).Trim();
            }

            if (filePath != null && newTab != null)
            {
                newTab.Tag = filePath;
            }
            if (newTab != null) newTab.Text = newTab.Text.TrimEnd('*');
        }

        private void ParseTile(TrackerContainer container, List<string> tileLines)
        {
            var tileWrapper = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 8) };
            var tile = new TrackerTile(container) { Dock = DockStyle.Fill };
            
            foreach (string line in tileLines)
            {
                int colonIdx = line.IndexOf(": ");
                if (colonIdx > 0)
                {
                    string label = line.Substring(0, colonIdx).Trim();
                    string val = line.Substring(colonIdx + 2);
                    
                    if (label == "Tag#") tile.GetInputs()[0].Text = val;
                    else if (label == "Address") tile.GetInputs()[1].Text = val;
                    else if (label == "ResN") tile.GetInputs()[2].Text = val;
                    else if (label == "VisN" || label == "CompN") 
                    { 
                        tile.SetVisnLabel(label); 
                        tile.GetInputs()[3].Text = val; 
                    }
                    else if (label == "Phone") 
                    { 
                        tile.SetPhoneVisible(true); 
                        tile.GetInputs()[4].Text = val; 
                    }
                    else if (label == "Email") 
                    { 
                        tile.SetEmailVisible(true); 
                        tile.GetInputs()[5].Text = val; 
                    }
                    else if (label == "Note") tile.GetInputs()[6].Text = val;
                }
            }
            
            tileWrapper.Controls.Add(tile);
            container.Controls.Add(tileWrapper);
            tileWrapper.BringToFront();
            container.Tiles.Add(tile);
        }
        private TextBoxBase GetFocusedTextBox(Control container)
        {
            TextBoxBase tb = container as TextBoxBase;
            if (tb != null && tb.Focused) return tb;
            foreach (Control c in container.Controls)
            {
                if (c.ContainsFocus)
                {
                    return GetFocusedTextBox(c);
                }
            }
            return null;
        }

        private void DeleteWordBeforeCursor(TextBoxBase tb)
        {
            if (tb.SelectionStart > 0)
            {
                int start = tb.SelectionStart;
                int len = tb.SelectionLength;

                if (len > 0)
                {
                    tb.SelectedText = "";
                    return;
                }

                string text = tb.Text;
                int ptr = start - 1;
                
                while (ptr >= 0 && char.IsWhiteSpace(text[ptr])) ptr--;
                
                if (ptr >= 0)
                {
                    bool isAlphanumeric = char.IsLetterOrDigit(text[ptr]);
                    while (ptr >= 0 && (char.IsLetterOrDigit(text[ptr]) == isAlphanumeric) && !char.IsWhiteSpace(text[ptr]))
                    {
                        ptr--;
                    }
                }
                
                int deleteLen = start - (ptr + 1);
                if (deleteLen > 0)
                {
                    tb.Select(ptr + 1, deleteLen);
                    tb.SelectedText = "";
                }
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.V))
            {
                TextBoxBase activeTextBox = GetFocusedTextBox(this);
                if (activeTextBox != null && !activeTextBox.ReadOnly)
                {
                    if (Clipboard.ContainsText(TextDataFormat.Text))
                    {
                        activeTextBox.SelectedText = Clipboard.GetText(TextDataFormat.Text);
                    }
                    return true;
                }
            }
            if (keyData == (Keys.Control | Keys.Back))
            {
                TextBoxBase activeTextBox = GetFocusedTextBox(this);
                if (activeTextBox != null && !activeTextBox.ReadOnly)
                {
                    DeleteWordBeforeCursor(activeTextBox);
                    return true;
                }
            }
            if (keyData == (Keys.Control | Keys.Oemplus) || keyData == (Keys.Control | Keys.Add))
            {
                Zoom(1);
                return true;
            }
            if (keyData == (Keys.Control | Keys.OemMinus) || keyData == (Keys.Control | Keys.Subtract))
            {
                Zoom(-1);
                return true;
            }

            if (keyData == (Keys.Control | Keys.O))
            {
                OpenFile(null, null);
                return true;
            }

            if (keyData == (Keys.Control | Keys.S))
            {
                SaveCurrentTabData(null, null);
                return true;
            }

            if (keyData == (Keys.Control | Keys.F))
            {
                findPanel.Visible = true;
                txtFind.Focus();
                txtFind.SelectAll();
                PerformSearch();
                return true;
            }
            
            if (keyData == Keys.Escape && findPanel.Visible)
            {
                findPanel.Visible = false;
                foreach (var m in currentMatches) m.Control.SelectionLength = 0;
                var currentTab1 = mainTabControl.SelectedTab;
                if (currentTab1 != null && currentTab1.Controls.Count > 0)
                    currentTab1.Controls[0].Focus();
                return true;
            }

            if (keyData == Keys.Enter && findPanel.Visible && txtFind.Focused)
            {
                GoToMatch(1);
                return true;
            }

            var currentTab = mainTabControl.SelectedTab;
            TrackerContainer container = currentTab != null && currentTab.Controls.Count > 0 ? currentTab.Controls[0] as TrackerContainer : null;
            RichTextBox notepad = currentTab != null && currentTab.Controls.Count > 0 ? currentTab.Controls[0] as RichTextBox : null;

            if (container != null)
            {
                if (keyData == (Keys.Shift | Keys.Enter))
                {
                    container.AddNewTile();
                    return true;
                }
                
                TrackerTile activeTile = container.GetActiveTile();
                if (activeTile != null)
                {
                    if (keyData == (Keys.Alt | Keys.C))
                    {
                        activeTile.SetVisnLabel("CompN");
                        return true;
                    }
                    else if (keyData == (Keys.Alt | Keys.V))
                    {
                        activeTile.SetVisnLabel("VisN");
                        return true;
                    }
                    else if (keyData == (Keys.Alt | Keys.P))
                    {
                        activeTile.TogglePhoneField();
                        return true;
                    }
                    else if (keyData == (Keys.Alt | Keys.E))
                    {
                        activeTile.ToggleEmailField();
                        return true;
                    }
                }
            }
            else if (notepad != null)
            {
                if (keyData == (Keys.Control | Keys.T))
                {
                    AddNotepadTab("New Tab");
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    public class TrackerContainer : Panel
    {
        public List<TrackerTile> Tiles { get; private set; }
        private MainForm parent;

        public TrackerContainer(MainForm parent)
        {
            this.Tiles = new List<TrackerTile>();
            this.parent = parent;
            this.AutoScroll = true;
            this.BackColor = SystemColors.Control;
            this.Padding = new Padding(4);
            AddNewTile();
        }

        public void MarkDirty()
        {
            TabPage page = this.Parent as TabPage;
            if (page != null)
            {
                if (!page.Text.EndsWith("*")) page.Text += "*";
            }
        }

        public void AddNewTile()
        {
            var tileWrapper = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 8) };
            var tile = new TrackerTile(this) { Dock = DockStyle.Fill };
            
            tileWrapper.Controls.Add(tile);
            this.Controls.Add(tileWrapper);
            tileWrapper.BringToFront(); 
            
            Tiles.Add(tile);
            this.ScrollControlIntoView(tileWrapper);
            tile.FocusFirst();
            MarkDirty();
        }

        public TrackerTile GetActiveTile()
        {
            foreach (var t in Tiles)
            {
                if (t.ContainsFocus) return t;
            }
            return Tiles.Count > 0 ? Tiles[Tiles.Count - 1] : null;
        }
    }

    public class TrackerTile : TableLayoutPanel
    {
        private Label lblVisn;
        
        private Label lblPhone;
        private RichTextBox txtPhone;
        private Label lblEmail;
        private RichTextBox txtEmail;

        private List<RichTextBox> inputs = new List<RichTextBox>();
        private TrackerContainer container;

        public TrackerTile(TrackerContainer container)
        {
            this.container = container;
            this.BorderStyle = BorderStyle.FixedSingle;
            this.BackColor = Color.White;
            
            this.AutoSize = true;
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.Padding = new Padding(2);
            
            this.ColumnCount = 2;
            this.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            this.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            this.RowCount = 7;
            for (int i = 0; i < 7; i++)
                this.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            AddField("Tag#", 0);
            AddField("Address", 1);
            AddField("ResN", 2);
            
            lblVisn = AddField("VisN", 3);
            
            lblPhone = AddField("Phone", 4);
            txtPhone = inputs[4];
            lblPhone.Visible = false;
            txtPhone.Visible = false;
            
            lblEmail = AddField("Email", 5);
            txtEmail = inputs[5];
            lblEmail.Visible = false;
            txtEmail.Visible = false;
            
            AddField("Note", 6);
        }

        private Label AddField(string labelText, int row)
        {
            var lbl = new Label { 
                Text = labelText, 
                AutoSize = true, 
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(3, 3, 3, 3)
            };
            
            var txt = new RichTextBox { 
                Dock = DockStyle.Fill, 
                Margin = new Padding(3, 3, 3, 3),
                HideSelection = false,
                Multiline = false,
                ScrollBars = RichTextBoxScrollBars.None,
                WordWrap = false
            };
            txt.TextChanged += (s, e) => container.MarkDirty();
            
            this.Controls.Add(lbl, 0, row);
            this.Controls.Add(txt, 1, row);
            inputs.Add(txt);
            return lbl;
        }

        public void FocusFirst()
        {
            if (inputs.Count > 0) inputs[0].Focus();
        }

        public string GetVisnLabel()
        {
            return lblVisn.Text;
        }

        public void SetVisnLabel(string text)
        {
            lblVisn.Text = text;
            container.MarkDirty();
        }

        public bool IsPhoneVisible() { return lblPhone.Visible; }
        public void SetPhoneVisible(bool visible) 
        { 
            lblPhone.Visible = visible; 
            txtPhone.Visible = visible;
            container.MarkDirty();
        }

        public bool IsEmailVisible() { return lblEmail.Visible; }
        public void SetEmailVisible(bool visible) 
        { 
            lblEmail.Visible = visible; 
            txtEmail.Visible = visible;
            container.MarkDirty();
        }

        public void TogglePhoneField()
        {
            SetPhoneVisible(!lblPhone.Visible);
        }

        public void ToggleEmailField()
        {
            SetEmailVisible(!lblEmail.Visible);
        }

        public List<RichTextBox> GetInputs()
        {
            return inputs;
        }

        public string GetDataString()
        {
            string res = "";
            for (int r = 0; r < this.RowCount; r++)
            {
                Label lbl = (Label)this.GetControlFromPosition(0, r);
                RichTextBox txt = (RichTextBox)this.GetControlFromPosition(1, r);
                
                if (lbl != null && txt != null && lbl.Visible)
                {
                    res += string.Format("{0}: {1}\r\n", lbl.Text, txt.Text);
                }
            }
            return res.TrimEnd();
        }
    }

    public static class CenteredMessageBox
    {
        public static DialogResult Show(Form owner, string text, string caption, MessageBoxButtons buttons)
        {
            using (Form form = new Form())
            {
                form.Text = caption;
                form.Size = new Size(340, 160);
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.ShowIcon = false;
                form.ShowInTaskbar = false;
                form.Font = new Font("Segoe UI", 9f);

                Label lbl = new Label() { 
                    Text = text, 
                    AutoSize = false, 
                    TextAlign = ContentAlignment.MiddleCenter, 
                    Dock = DockStyle.Top, 
                    Height = 70, 
                    Padding = new Padding(15, 10, 15, 0) 
                };
                form.Controls.Add(lbl);

                FlowLayoutPanel flp = new FlowLayoutPanel() { 
                    Dock = DockStyle.Bottom, 
                    Height = 45, 
                    FlowDirection = FlowDirection.RightToLeft, 
                    Padding = new Padding(0, 5, 15, 0),
                    BackColor = SystemColors.ControlLight
                };
                form.Controls.Add(flp);

                if (buttons == MessageBoxButtons.YesNoCancel)
                {
                    Button btnCancel = new Button() { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80, Height = 28 };
                    Button btnNo = new Button() { Text = "No", DialogResult = DialogResult.No, Width = 80, Height = 28 };
                    Button btnYes = new Button() { Text = "Yes", DialogResult = DialogResult.Yes, Width = 80, Height = 28 };
                    flp.Controls.Add(btnCancel);
                    flp.Controls.Add(btnNo);
                    flp.Controls.Add(btnYes);
                }
                else if (buttons == MessageBoxButtons.OK)
                {
                    Button btnOK = new Button() { Text = "OK", DialogResult = DialogResult.OK, Width = 80, Height = 28 };
                    flp.Controls.Add(btnOK);
                }

                return form.ShowDialog(owner);
            }
        }
    }
}
