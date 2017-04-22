﻿using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ServiceMonitor
{
    public partial class MonitorView : Form
    {
        MonitorController _controller;

        public MonitorView()
        {
            _controller = new MonitorController(this);
            _controller.OnProcessCreate += OnProcessCreate;

            InitializeComponent();

            PaintTabControl();
            
        }

        void PaintTabControl()
        {
            tabMain.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabMain.DrawItem += (sender, e) =>
            {
                var model = SafeGetModel(tabMain.TabPages[e.Index]);

                Rectangle myTabRect = tabMain.GetTabRect(e.Index);

                Brush textBrush = null;
                Brush bgBrush = null;
    
                if ( model.Running )
                {
                    bgBrush = ColorSettings.ColorToBrush(Color.Green);
                    textBrush = ColorSettings.ColorToBrush(Color.White);
                }
                else if (model.SelfExit)
                {
                    bgBrush = ColorSettings.ColorToBrush(Color.Red);
                    textBrush = ColorSettings.ColorToBrush(Color.White);
                }
             

                if (e.Index == tabMain.SelectedIndex)
                {
                    textBrush = ColorSettings.ColorToBrush(Color.White);
                    bgBrush = ColorSettings.ColorToBrush(Color.Black);
                }
                else if (textBrush == null)
                {
                    textBrush = ColorSettings.ColorToBrush(Color.Black);
                }

                if (bgBrush != null )
                {
                    e.Graphics.FillRectangle(bgBrush, myTabRect);
                }
                

                //先添加TabPage属性      
                e.Graphics.DrawString(tabMain.TabPages[e.Index].Text, this.Font, textBrush, myTabRect.X + 2, myTabRect.Y + 2);                

            };
        }



        #region Model获取封装,基础功能封装

        ProcessModel SafeGetModel(object obj)
        {
            var model = _controller.GetModelByObject(obj);
            if (model == null)
            {
                return new ProcessModel();
            }

            return model;
        }

        ProcessModel SafeGetCurrTableModel()
        {
            return SafeGetModel(tabMain.SelectedTab);
        }

        // 命名规则: 与svc同目录下,  svc.exe 对应的批处理是 svc_Build.bat
        void RunSvcShell(ProcessModel svcModel, bool startAfterDone)
        {
            if (!svcModel.Valid)
                return;

            // 还在跑的进程, 必须停下来
            if (svcModel.Running)
            {
                svcModel.Stop();
            }

            var buildcmd = Path.Combine(Path.GetDirectoryName(svcModel.FileName), Path.GetFileNameWithoutExtension(svcModel.FileName) + "_Build") + ".bat";

            var shellModel = new ProcessModel();
            shellModel.FileName = buildcmd;
            shellModel.invoker = this;
            shellModel.CanStop = false;

            shellModel.OnStart += (m) =>
            {
                m.WriteLog(Color.Yellow, "启动Shell: " + buildcmd);
            };

            Action<ProcessModel> stopProc = (m) =>
            {
                m.WriteLog(Color.Yellow, "结束Shell: " + buildcmd);

                // 编译正常时, 启动进程
                if (startAfterDone && shellModel.ExitCode == 0)
                {
                    svcModel.Start();                    
                }
            };


            shellModel.OnStop += stopProc;
            shellModel.OnExit += stopProc;

            shellModel.OnLog += svcModel.OnLog;
            shellModel.OnError += svcModel.OnError;

            shellModel.Start();

            RefreshButtonStatus();
        }

        #endregion

        #region Process创建,开启,关闭行为

        object OnProcessCreate(ProcessModel model)
        {
            var name = Path.GetFileNameWithoutExtension(model.FileName);

            var page = new TabPage(name);            
            page.ContextMenuStrip = logMenu;
            var logview = new LogView();
            page.ToolTipText = model.FileName;

            page.Controls.Add(logview);
            tabMain.TabPages.Add(page);

            model.Index = tabMain.TabPages.IndexOf(page);

            tabMain.SelectedTab = page;
            tabMain.MouseClick += (sender, e) =>
            {
                if ( e.Button == System.Windows.Forms.MouseButtons.Right )
                {
                    tabMenu.Show(tabMain, e.Location);
                }
            };

            Action<ProcessModel, Color, string> logProc = (m, c, data) =>
            {
                logview.AddLog(c, data, model.AutoScroll);
            };

            model.OnStart += OnProcessStart;
            model.OnStop += OnProcessStop;
            model.OnExit += OnProcessExit;

            model.OnLog += logProc;
            model.OnError += logProc;
            model.view = logview;

            model.OnClear += delegate()
            {
                logview.Items.Clear();
            };

            model.OnGetData += ( index ) =>
            {
                return logview.Items[index] as LogData;
            };


            model.OnGetDataCount += ( ) =>
            {
                return logview.Items.Count;
            };

            model.OnGetAllLog += delegate()
            {
                return logview.AllLogToString();
            };

            model.OnGetSelectedContent += delegate()
            {
                var logdata = logview.SelectedItem as LogData;
                if (logdata == null)
                    return string.Empty;

                return logdata.Text;
            };

            model.WriteLog(Color.Yellow, "就绪");

            return page;
        }


        void OnProcessStart(ProcessModel model )
        {            
            model.WriteLog(Color.Yellow, "进程启动 ");

            RefreshButtonStatus();

            tabMain.Refresh();
        }

        void OnProcessStop(ProcessModel model)
        {
            tabMain.Refresh();
        }

        void OnProcessExit(ProcessModel model)
        {
            RefreshButtonStatus();

            tabMain.Refresh();

            model.WriteLog(Color.Yellow, string.Format("进程结束({0})", model.ExitCode) );
        }


        #endregion

        #region 主面板MainForm及按钮行为

        void MainForm_Load(object sender, EventArgs e)
        {
            _controller.Init();
        }


        private void MainForm_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (string file in files)
            {

                _controller.AddProcess(new TabInfo { FileName = file });
            }
        }
        private void MainForm_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop, false))
            {
                e.Effect = DragDropEffects.All;
            }

        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            _controller.Exit();
        }


        private void btnStart_Click(object sender, EventArgs e)
        {
            SafeGetCurrTableModel().Start();

            RefreshButtonStatus();
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            SafeGetCurrTableModel().Stop();

            RefreshButtonStatus();
        }
        private void btnStartAll_Click(object sender, EventArgs e)
        {
            _controller.StartAllProcess();

            RefreshButtonStatus();
        }

        private void btnStopAll_Click(object sender, EventArgs e)
        {
            _controller.StopAllProcess(false);

            RefreshButtonStatus();
        }

        private void tabMain_SelectedIndexChanged(object sender, EventArgs e)
        {
            RefreshButtonStatus();
        }

        private void btnSetWorkDir_Click(object sender, EventArgs e)
        {
            var model = SafeGetCurrTableModel();

            var dialog = new WorkDirDialog(model.WorkDir);
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                model.WorkDir = dialog.WorkDir;
            }
        }

        bool _disableTextNotify;
        void RefreshButtonStatus( )
        {
            var pm = SafeGetCurrTableModel();

            btnStart.Enabled = !pm.Running;

            if ( pm.CanStop )
            {
                btnStop.Enabled = pm.Running;
            }
            else
            {
                btnStop.Enabled = false;
            }
            

            _disableTextNotify = true;
            txtArgs.Text = pm.Args;
            _disableTextNotify = false;
        }


        private void txtArgs_TextChanged(object sender, EventArgs e)
        {
            if (_disableTextNotify)
                return;

            SafeGetCurrTableModel().Args = txtArgs.Text;            
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            SafeGetCurrTableModel().ClearLog();
        }

        #endregion

        #region 日志菜单

        private void ClearToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SafeGetCurrTableModel().ClearLog();
        }


        private void CopyLineTextToolStripMenuItem_Click(object sender, EventArgs e)
        {

            var text = SafeGetCurrTableModel().GetSelectedContext();
            Clipboard.SetText(text);
        }

        private void ShowLineTextToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var text = SafeGetCurrTableModel().GetSelectedContext();

            var dialog = new TextDialog(text);
            dialog.ShowDialog();
        }

        private void LogSaveToFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Filter = " txt files(*.txt)|*.txt|All files(*.*)|*.*";  
            dialog.RestoreDirectory = true ;
            if ( dialog.ShowDialog( ) == System.Windows.Forms.DialogResult.OK )
            {
                File.WriteAllText(dialog.FileName, SafeGetCurrTableModel().GetAllLog() );
                
            }
        }

        private void ClearAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _controller.ClearAllProcessLog();
        }

        private void AutoScrollToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var item = sender as ToolStripMenuItem;
            item.Checked = !item.Checked;

            SafeGetCurrTableModel( ).AutoScroll = item.Checked;
        }

        private void ManualStartToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var item = sender as ToolStripMenuItem;
            item.Checked = !item.Checked;

            SafeGetCurrTableModel().ManualControl = item.Checked;
        }


        private void mnuTab_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var model = SafeGetCurrTableModel();
            if (!string.IsNullOrEmpty(model.FileName))
            {
                AutoScrollToolStripMenuItem.Checked = model.AutoScroll;
                ManualStartToolStripMenuItem.Checked = model.ManualControl;
                SearchToolStripMenuItem.Enabled = _search == null;
            }
           
        }

        SearchDialog _search;

        private void SearchToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_search != null)
                return;

            _search = new SearchDialog( SafeGetCurrTableModel() );
            _search.FormClosed += (s, ee) =>
            {
                _search = null;
            };
            _search.Show(this);
        }

        // 编译并运行
        private void BuildRunFToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RunSvcShell(SafeGetCurrTableModel(), true);
        }

        // 编译
        private void BuildToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RunSvcShell(SafeGetCurrTableModel(), false);
        }

        #endregion

        #region Tab菜单

        private void AddTabToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "exe files (*.exe)|*.exe|bat files(*.bat)|*.bat";
            dialog.FilterIndex = 1;
            dialog.RestoreDirectory = true;

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                _controller.AddProcess(new TabInfo { FileName = dialog.FileName });
            }
        }

        private void CloseTabToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var tab = tabMain.SelectedTab;
            if (tab != null)
            {
                SafeGetCurrTableModel().Stop();

                tabMain.TabPages.Remove(tab);
                _controller.RemoveProcess(tab);
            }
        }

        private void CopyTabToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var model = SafeGetCurrTableModel();
            if (!string.IsNullOrEmpty(model.FileName))
            {
                var tabinfo = new TabInfo { FileName = model.FileName, Args = model.Args, ManualControl = model.ManualControl };
                _controller.AddProcess(tabinfo);
            }
        }

        private void OpenDirToolStripMenuItem_Click_1(object sender, EventArgs e)
        {
            var model = SafeGetCurrTableModel();
            if (!string.IsNullOrEmpty(model.FileName))
            {
                Process.Start("explorer.exe", Path.GetDirectoryName(model.FileName));
            }
        }


        private void MoveLeftToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MoveTab(-1);
        }

        private void MoveRightToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MoveTab(1);
        }

        void MoveTab(int delta)
        {
            var index = tabMain.SelectedIndex;

            if ( index >= tabMain.TabPages.Count - 1 && delta > 0 )
            {
                return;
            }

            if ( index <= 0 && delta < 0 )
            {
                return;
            }

            var tab = tabMain.SelectedTab;

            tabMain.TabPages.RemoveAt(index);

            tabMain.TabPages.Insert(index + delta, tab);
            tabMain.SelectedIndex = index + delta;

            // 与谁换
            var slibing = SafeGetModel(tabMain.TabPages[index]);
            // 更新索引
            slibing.Index = index;

            // 主动换的人更新索引
            var model = SafeGetCurrTableModel();
            model.Index = tabMain.TabPages.IndexOf(tab);

        }



        #endregion










  



   
        
       
 

    }
}
