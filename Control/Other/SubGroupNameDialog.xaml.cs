using System;
using System.Windows;

namespace GeekDesk.Control.Other
{
    /// <summary>
    /// 二级分组命名弹窗
    /// </summary>
    public partial class SubGroupNameDialog
    {
        public HandyControl.Controls.Dialog dialog;

        /// <summary>
        /// 保存成功后的回调  传入分组名称
        /// </summary>
        public Action<string> Saved;

        /// <summary>
        /// 传入初始名称  用于重命名
        /// </summary>
        /// <param name="initName"></param>
        public SubGroupNameDialog(string initName = "")
        {
            InitializeComponent();
            SubName.Text = initName;
            Loaded += (s, e) =>
            {
                SubName.Focus();
                SubName.SelectAll();
            };
        }

        /// <summary>
        /// 保存
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Save(object sender, RoutedEventArgs e)
        {
            string name = SubName.Text == null ? "" : SubName.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                HandyControl.Controls.Growl.Warning("分组名称不能为空!", "MainWindowGrowl");
                return;
            }
            dialog.Close();
            if (Saved != null)
            {
                Saved(name);
            }
        }
    }
}
