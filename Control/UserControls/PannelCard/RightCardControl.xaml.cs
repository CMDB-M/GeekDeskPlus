using DraggAnimatedPanelExample;
using GeekDesk.Constant;
using GeekDesk.Control.Other;
using GeekDesk.Control.Windows;
using GeekDesk.Plugins.EveryThing;
using GeekDesk.Util;
using GeekDesk.ViewModel;
using GeekDesk.ViewModel.Temp;
using HandyControl.Controls;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace GeekDesk.Control.UserControls.PannelCard
{
    /// <summary>
    /// RightCardControl.xaml 的交互逻辑
    /// </summary>
    public partial class RightCardControl : UserControl
    {
        private AppData appData = MainWindow.appData;

        ListBoxDragDropManager<IconInfo> dragMgr;

        //private Thread dropCheckThread = null;

        /// <summary>
        /// 拖拽到分组标签上的图标  Drop时使用
        /// </summary>
        private IconInfo chipDragIcon;

        /// <summary>
        /// 右键"移动到分组"菜单当前操作的图标
        /// </summary>
        private IconInfo moveToMenuIcon;

        /// <summary>
        /// 悬浮提示(路径信息Popup)请求序号  鼠标移出/开始拖拽时自增  使尚未执行的延迟打开请求失效
        /// </summary>
        private int poptipRequestId = 0;

        public RightCardControl()
        {
            InitializeComponent();
            this.Loaded += RightCardControl_Loaded;

        }

        private void RightCardControl_Loaded(object sender, RoutedEventArgs e)
        {
            this.dragMgr = new ListBoxDragDropManager<IconInfo>(this.IconListBox);
        }


        #region 二级分组

        /// <summary>
        /// 程序化同步芯片选中态标志  防止IsChecked赋值重入触发切换逻辑
        /// </summary>
        private bool syncingChips = false;

        /// <summary>
        /// 获取当前选中菜单在MenuList中的索引
        /// 优先读取左侧列表实时选中索引: SelectionChanged事件先于TwoWay绑定回写SelectedMenuIndex,
        /// 在切换菜单的瞬间配置索引还是旧菜单, 直接读配置索引会把图标列表/分组标签解析到上一个菜单(经典串菜单bug)
        /// </summary>
        /// <returns>找不到返回-1</returns>
        public static int GetCurrentMenuIndex()
        {
            AppData ad = MainWindow.appData;
            if (ad == null || ad.MenuList == null || ad.MenuList.Count == 0) return -1;
            int index = -1;
            try
            {
                LeftCardControl leftCard = MainWindow.mainWindow != null ? MainWindow.mainWindow.LeftCard : null;
                if (leftCard != null && leftCard.MenuListBox != null)
                {
                    index = leftCard.MenuListBox.SelectedIndex;
                }
            }
            catch (Exception) { }
            if (index < 0 || index >= ad.MenuList.Count)
            {
                index = ad.AppConfig.SelectedMenuIndex;
            }
            if (index < 0 || index >= ad.MenuList.Count)
            {
                //最后兜底: 用当前显示的列表反查所属菜单(菜单重命名等选中态暂时丢失的场景)
                ObservableCollection<IconInfo> displayed = ad.AppConfig.SelectedMenuIcons;
                if (displayed != null)
                {
                    for (int i = 0; i < ad.MenuList.Count; i++)
                    {
                        MenuInfo mi = ad.MenuList[i];
                        if (ReferenceEquals(mi.IconList, displayed))
                        {
                            index = i;
                            break;
                        }
                        foreach (SubGroupInfo sg in mi.SubGroups)
                        {
                            if (sg != null && ReferenceEquals(sg.IconList, displayed))
                            {
                                index = i;
                                break;
                            }
                        }
                        if (index >= 0) break;
                    }
                }
            }
            if (index < 0 || index >= ad.MenuList.Count) return -1;
            return index;
        }

        /// <summary>
        /// 获取当前选中的菜单  取不到返回null
        /// </summary>
        /// <returns></returns>
        public static MenuInfo GetSelectedMenu()
        {
            AppData ad = MainWindow.appData;
            int index = GetCurrentMenuIndex();
            if (ad == null || ad.MenuList == null || index < 0 || index >= ad.MenuList.Count) return null;
            return ad.MenuList[index];
        }

        /// <summary>
        /// 当前选中的分组ID  仅当该分组确实存在于当前菜单时返回  否则返回null
        /// </summary>
        /// <returns></returns>
        public static string GetSelectedSubGroupIdSafe()
        {
            string subGroupId = MainWindow.appData.AppConfig.SelectedSubGroupId_NoWrite;
            if (string.IsNullOrEmpty(subGroupId)) return null;
            return GetSubGroupById(subGroupId) != null ? subGroupId : null;
        }

        /// <summary>
        /// 将新图标加入当前显示的列表(默认或当前分组)  供拖放/URL/系统项目等添加入口统一复用
        /// </summary>
        /// <param name="icon"></param>
        public static void AddIconToCurrentList(IconInfo icon)
        {
            if (icon == null) return;
            MenuInfo menu = GetSelectedMenu();
            if (menu == null)
            {
                LogUtil.WriteErrorLog("添加项目失败: 无法确定当前菜单!");
                return;
            }
            //分组ID经过有效性校验  无效时不入组(避免把图标加到其它菜单的分组里)
            string subGroupId = GetSelectedSubGroupIdSafe();
            icon.SubGroupId_NoWrite = subGroupId;
            GetTargetList(menu, subGroupId).Add(icon);
            //同步显示  保证新图标出现在用户正在看的列表里
            if (MainWindow.mainWindow != null && MainWindow.mainWindow.RightCard != null)
            {
                MainWindow.mainWindow.RightCard.RefreshSubGroupBar();
            }
        }

        /// <summary>
        /// 根据ID获取分组  没有返回null(仅在当前菜单内查找)
        /// </summary>
        /// <param name="subGroupId"></param>
        /// <returns></returns>
        public static SubGroupInfo GetSubGroupById(string subGroupId)
        {
            return GetSubGroup(GetSelectedMenu(), subGroupId);
        }

        /// <summary>
        /// 在指定菜单内按ID获取分组  没有返回null
        /// </summary>
        /// <param name="menu"></param>
        /// <param name="subGroupId"></param>
        /// <returns></returns>
        public static SubGroupInfo GetSubGroup(MenuInfo menu, string subGroupId)
        {
            if (menu == null || string.IsNullOrEmpty(subGroupId)) return null;
            foreach (SubGroupInfo sg in menu.SubGroups)
            {
                if (sg != null && subGroupId.Equals(sg.SubId))
                {
                    return sg;
                }
            }
            return null;
        }

        /// <summary>
        /// 获取分组对应的目标列表(默认列表或组内列表)
        /// </summary>
        /// <param name="menu"></param>
        /// <param name="targetGroupId">null=默认列表</param>
        /// <returns></returns>
        public static ObservableCollection<IconInfo> GetTargetList(MenuInfo menu, string targetGroupId)
        {
            if (menu == null) return null;
            if (string.IsNullOrEmpty(targetGroupId)) return menu.IconList;
            SubGroupInfo sg = GetSubGroup(menu, targetGroupId);
            return sg == null ? menu.IconList : sg.IconList;
        }

        /// <summary>
        /// 从菜单的默认列表和所有分组中移除图标  并清除分组归属
        /// </summary>
        /// <param name="menu"></param>
        /// <param name="icon"></param>
        public static void RemoveIconFromMenu(MenuInfo menu, IconInfo icon)
        {
            if (menu == null || icon == null) return;
            menu.IconList.Remove(icon);
            foreach (SubGroupInfo sg in menu.SubGroups)
            {
                if (sg != null && sg.IconList != null) sg.IconList.Remove(icon);
            }
            icon.SubGroupId_NoWrite = null;
        }

        /// <summary>
        /// 查找图标真实所在的菜单(含各分组)  找不到返回null
        /// </summary>
        /// <param name="icon"></param>
        /// <returns></returns>
        public static MenuInfo FindMenuContainingIcon(IconInfo icon)
        {
            AppData ad = MainWindow.appData;
            if (ad == null || ad.MenuList == null || icon == null) return null;
            foreach (MenuInfo mi in ad.MenuList)
            {
                if (mi == null) continue;
                if (mi.IconList.Contains(icon)) return mi;
                foreach (SubGroupInfo sg in mi.SubGroups)
                {
                    if (sg != null && sg.IconList != null && sg.IconList.Contains(icon)) return mi;
                }
            }
            return null;
        }

        /// <summary>
        /// 刷新右侧显示: 按"真实选中的菜单 + 该菜单内有效的分组"重新绑定图标列表和分组标签行
        /// 所有菜单切换/分组切换/图标增删后都应调用本方法  它是显示状态的唯一同步入口
        /// </summary>
        public void RefreshSubGroupBar()
        {
            MenuInfo menu = GetSelectedMenu();
            if (menu == null)
            {
                //无法确定当前菜单(菜单重命名瞬间等)  保持原显示, 仅收起标签行
                SubGroupBar.Visibility = Visibility.Collapsed;
                return;
            }

            bool encrypted = menu.IsEncrypt;
            //分组ID只在所属菜单内有效  否则视为默认列表(修复"换菜单后仍显示上一个菜单分组"的问题)
            string selectedId = appData.AppConfig.SelectedSubGroupId_NoWrite;
            if (GetSubGroup(menu, selectedId) == null) selectedId = null;
            bool show = menu.MenuType == MenuType.NORMAL
                && menu.SubGroups.Count > 0
                && !encrypted;

            try
            {
                //同步期间芯片Checked/Click事件不触发切换逻辑
                syncingChips = true;
                SubGroupBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                //无论显示与否都重置芯片数据源  防止残留上一个菜单的分组芯片
                SubGroupChips.ItemsSource = null;
                DefaultChip.IsChecked = false;

                if (show)
                {
                    SubGroupChips.ItemsSource = menu.SubGroups;
                    //分组芯片的选中态由SubGroupChip_Loaded恢复  这里只同步"默认"标签
                    DefaultChip.IsChecked = selectedId == null;
                }

                //加密菜单的显示列表由解锁流程控制  这里不介入, 避免绕过密码直接显示图标
                if (!encrypted)
                {
                    appData.AppConfig.SelectedSubGroupId_NoWrite = selectedId;
                    //始终把显示列表重新指向当前菜单(或其当前分组)
                    ObservableCollection<IconInfo> target = GetTargetList(menu, selectedId);
                    if (target != null && appData.AppConfig.SelectedMenuIcons != target)
                    {
                        appData.AppConfig.SelectedMenuIcons = target;
                    }
                }
                else
                {
                    appData.AppConfig.SelectedSubGroupId_NoWrite = null;
                }
            }
            finally
            {
                syncingChips = false;
            }
        }

        /// <summary>
        /// 分组芯片加载时  同步选中状态(ItemsSource重置后芯片重建)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SubGroupChip_Loaded(object sender, RoutedEventArgs e)
        {
            SetChipChecked(sender as RadioButton, GetChipShouldCheck(sender as RadioButton));
        }

        /// <summary>
        /// 该芯片是否应处于选中态
        /// </summary>
        /// <param name="chip"></param>
        /// <returns></returns>
        private bool GetChipShouldCheck(RadioButton chip)
        {
            SubGroupInfo sg = chip == null ? null : chip.DataContext as SubGroupInfo;
            if (sg == null) return false;
            string selectedId = appData.AppConfig.SelectedSubGroupId_NoWrite;
            return selectedId != null && selectedId.Equals(sg.SubId);
        }

        /// <summary>
        /// 程序化设置芯片选中态  携带同步保护  不会触发切换逻辑
        /// </summary>
        /// <param name="chip"></param>
        /// <param name="value"></param>
        private void SetChipChecked(RadioButton chip, bool value)
        {
            if (chip == null || chip.IsChecked == value) return;
            bool wasSyncing = syncingChips;
            syncingChips = true;
            try
            {
                chip.IsChecked = value;
            }
            finally
            {
                if (!wasSyncing) syncingChips = false;
            }
        }

        /// <summary>
        /// 分组芯片被点击(含取消选中)  统一处理选中态  不依赖GroupName互斥
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SubGroupChip_Click(object sender, RoutedEventArgs e)
        {
            if (syncingChips) return;
            RadioButton chip = sender as RadioButton;
            if (chip == null || chip.DataContext == null) return;
            SelectSubGroup((chip.DataContext as SubGroupInfo).SubId);
        }

        /// <summary>
        /// 分组芯片Checked事件(键盘等触发)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SubGroupChip_Checked(object sender, RoutedEventArgs e)
        {
            if (syncingChips) return;
            RadioButton chip = sender as RadioButton;
            if (chip == null || chip.DataContext == null || chip.IsChecked != true) return;
            SelectSubGroup((chip.DataContext as SubGroupInfo).SubId);
        }

        /// <summary>
        /// 切换到指定分组  并统一刷新所有芯片的选中态
        /// </summary>
        /// <param name="subGroupId"></param>
        private void SelectSubGroup(string subGroupId)
        {
            appData.AppConfig.SelectedSubGroupId_NoWrite = subGroupId;
            MenuInfo menu = GetSelectedMenu();
            ObservableCollection<IconInfo> targetList = menu == null
                ? null
                : GetTargetList(menu, subGroupId);
            if (targetList != null && appData.AppConfig.SelectedMenuIcons != targetList)
            {
                appData.AppConfig.SelectedMenuIcons = targetList;
            }
            //统一同步所有芯片选中态(GroupName跨ItemsControl不互斥  由代码保证)
            SetChipChecked(DefaultChip, string.IsNullOrEmpty(subGroupId));
            if (SubGroupChips.ItemsSource != null)
            {
                foreach (object item in SubGroupChips.Items)
                {
                    DependencyObject container = SubGroupChips.ItemContainerGenerator.ContainerFromItem(item);
                    RadioButton chip = FindVisualChild<RadioButton>(container);
                    if (chip != null)
                    {
                        SetChipChecked(chip, GetChipShouldCheck(chip));
                    }
                }
            }
        }

        /// <summary>
        /// 在视觉树中查找指定类型的第一个子元素
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parent"></param>
        /// <returns></returns>
        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed)
                {
                    return typed;
                }
                T result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        /// <summary>
        /// 点击"默认"标签  显示未分组图标
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DefaultChip_Checked(object sender, RoutedEventArgs e)
        {
            if (syncingChips) return;
            //分组标签互斥切换时  未选中分组的Unchecked不处理  只响应新选中的
            if (DefaultChip.IsChecked != true) return;
            MenuInfo menu = GetSelectedMenu();
            if (menu == null) return;
            appData.AppConfig.SelectedSubGroupId_NoWrite = null;
            if (appData.AppConfig.SelectedMenuIcons != menu.IconList)
            {
                appData.AppConfig.SelectedMenuIcons = menu.IconList;
            }
            //取消所有分组芯片的选中态
            if (SubGroupChips.ItemsSource != null)
            {
                foreach (object item in SubGroupChips.Items)
                {
                    DependencyObject container = SubGroupChips.ItemContainerGenerator.ContainerFromItem(item);
                    RadioButton chip = FindVisualChild<RadioButton>(container);
                    if (chip != null && chip.IsChecked == true)
                    {
                        SetChipChecked(chip, false);
                    }
                }
            }
        }

        /// <summary>
        /// 新建分组
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SubGroupAdd_Click(object sender, RoutedEventArgs e)
        {
            MenuInfo menu = GetSelectedMenu();
            if (menu == null) return;
            SubGroupNameDialog nameDialog = new SubGroupNameDialog();
            nameDialog.Saved = (name) =>
            {
                //校验重名
                foreach (SubGroupInfo sg in menu.SubGroups)
                {
                    if (name.Equals(sg.SubName))
                    {
                        HandyControl.Controls.Growl.Warning("已存在同名分组: " + name, "MainWindowGrowl");
                        return;
                    }
                }
                SubGroupInfo newGroup = new SubGroupInfo
                {
                    SubId = System.Guid.NewGuid().ToString(),
                    SubName = name
                };
                menu.SubGroups.Add(newGroup);
                CommonCode.SaveAppData(MainWindow.appData, Constants.DATA_FILE_PATH);
                //新建后直接切入该分组(仅当用户仍停留在新建时的菜单)
                if (ReferenceEquals(menu, GetSelectedMenu()))
                {
                    appData.AppConfig.SelectedSubGroupId_NoWrite = newGroup.SubId;
                }
                RefreshSubGroupBar();
            };
            nameDialog.dialog = HandyControl.Controls.Dialog.Show(nameDialog, "MainWindowDialog");
        }

        /// <summary>
        /// 重命名分组
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SubGroupRename(object sender, RoutedEventArgs e)
        {
            SubGroupInfo sg = ((MenuItem)sender).Tag as SubGroupInfo;
            if (sg == null) return;
            MenuInfo menu = GetSelectedMenu();
            if (menu == null) return;
            SubGroupNameDialog nameDialog = new SubGroupNameDialog(sg.SubName);
            nameDialog.Saved = (name) =>
            {
                if (name.Equals(sg.SubName)) return;
                foreach (SubGroupInfo other in menu.SubGroups)
                {
                    if (other != sg && name.Equals(other.SubName))
                    {
                        HandyControl.Controls.Growl.Warning("已存在同名分组: " + name, "MainWindowGrowl");
                        return;
                    }
                }
                sg.SubName = name;
                CommonCode.SaveAppData(MainWindow.appData, Constants.DATA_FILE_PATH);
            };
            nameDialog.dialog = HandyControl.Controls.Dialog.Show(nameDialog, "MainWindowDialog");
        }

        /// <summary>
        /// 上移分组
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SubGroupMoveUp(object sender, RoutedEventArgs e)
        {
            MoveSubGroup((MenuItem)sender, -1);
        }

        /// <summary>
        /// 下移分组
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SubGroupMoveDown(object sender, RoutedEventArgs e)
        {
            MoveSubGroup((MenuItem)sender, 1);
        }

        private void MoveSubGroup(MenuItem mi, int offset)
        {
            SubGroupInfo sg = mi.Tag as SubGroupInfo;
            if (sg == null) return;
            MenuInfo menu = GetSelectedMenu();
            if (menu == null) return;
            ObservableCollection<SubGroupInfo> groups = menu.SubGroups;
            int oldIndex = groups.IndexOf(sg);
            int newIndex = oldIndex + offset;
            if (oldIndex < 0 || newIndex < 0 || newIndex >= groups.Count) return;
            groups.Move(oldIndex, newIndex);
            CommonCode.SaveAppData(MainWindow.appData, Constants.DATA_FILE_PATH);
            RefreshSubGroupBar();
        }

        /// <summary>
        /// 删除分组  组内图标退回默认列表
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SubGroupDelete(object sender, RoutedEventArgs e)
        {
            SubGroupInfo sg = ((MenuItem)sender).Tag as SubGroupInfo;
            if (sg == null) return;
            HandyControl.Controls.Growl.Ask("确认删除分组 [" + sg.SubName + "] 吗?\n组内图标将退回到默认列表, 不会丢失!", isConfirmed =>
            {
                if (!isConfirmed) return true;
                MenuInfo menu = GetSelectedMenu();
                if (menu == null) return true;
                //组内图标退回默认
                foreach (IconInfo icon in sg.IconList)
                {
                    icon.SubGroupId_NoWrite = null;
                    menu.IconList.Add(icon);
                }
                sg.IconList.Clear();
                menu.SubGroups.Remove(sg);
                if (appData.AppConfig.SelectedSubGroupId_NoWrite != null
                    && appData.AppConfig.SelectedSubGroupId_NoWrite.Equals(sg.SubId))
                {
                    appData.AppConfig.SelectedSubGroupId_NoWrite = null;
                }
                CommonCode.SaveAppData(MainWindow.appData, Constants.DATA_FILE_PATH);
                RefreshSubGroupBar();
                HandyControl.Controls.Growl.Success("分组 [" + sg.SubName + "] 已删除!", "MainWindowGrowl");
                return true;
            }, "MainWindowAskGrowl");
        }

        /// <summary>
        /// 图标拖到分组标签上方时  判断是否为图标拖拽
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SubGroupChip_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(typeof(IconInfo)) || e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        /// <summary>
        /// 图标拖入分组标签  显示提示
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SubGroupChip_DragEnter(object sender, DragEventArgs e)
        {
            //拖拽进入标签时作废未执行的图标路径提示请求  改为显示"移动至"提示
            poptipRequestId++;
            RadioButton chip = sender as RadioButton;
            object data = e.Data.GetData(typeof(IconInfo));
            bool isDefaultChip = chip == DefaultChip;
            string tip = "移动至:" + (isDefaultChip ? "默认" : ((SubGroupInfo)chip.DataContext).SubName);
            if (data is IconInfo)
            {
                chipDragIcon = (IconInfo)data;
                MyPoptipContent.Text = tip;
                MyPoptip.VerticalOffset = 30;
                MyPoptip.IsOpen = true;
            }
            else
            {
                chipDragIcon = null;
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    MyPoptipContent.Text = tip;
                    MyPoptip.VerticalOffset = 30;
                    MyPoptip.IsOpen = true;
                }
            }
        }

        /// <summary>
        /// 拖离分组标签  关闭提示
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SubGroupChip_DragLeave(object sender, DragEventArgs e)
        {
            chipDragIcon = null;
            ClosePoptip();
        }

        /// <summary>
        /// 拖放图标到分组标签  移入该组
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SubGroupChip_Drop(object sender, DragEventArgs e)
        {
            chipDragIcon = null;
            ClosePoptip();
            RadioButton chip = sender as RadioButton;
            if (chip == null) return;
            MenuInfo menu = GetSelectedMenu();
            if (menu == null) return;

            //目标组  默认标签=null
            string targetGroupId = null;
            if (chip != DefaultChip)
            {
                SubGroupInfo targetGroup = chip.DataContext as SubGroupInfo;
                if (targetGroup == null) return;
                targetGroupId = targetGroup.SubId;
            }

            try
            {
                IconInfo icon = (IconInfo)e.Data.GetData(typeof(IconInfo));
                if (icon != null)
                {
                    MoveIconToGroup(menu, icon, targetGroupId);
                }
                else
                {
                    //文件拖入  新建图标到目标组
                    Array dropObject = (System.Array)e.Data.GetData(DataFormats.FileDrop);
                    if (dropObject == null) return;
                    foreach (object obj in dropObject)
                    {
                        string path = (string)obj;
                        IconInfo newIcon = CommonCode.GetIconInfoByPath(path);
                        if (newIcon == null)
                        {
                            LogUtil.WriteErrorLog("添加项目失败，未能获取到项目图标:" + path);
                            break;
                        }
                        newIcon.SubGroupId_NoWrite = targetGroupId;
                        GetTargetList(menu, targetGroupId).Add(newIcon);
                    }
                    CommonCode.SortIconList();
                    CommonCode.SaveAppData(MainWindow.appData, Constants.DATA_FILE_PATH);
                    //切换到目标分组  让用户立刻看到刚拖入的图标
                    appData.AppConfig.SelectedSubGroupId_NoWrite = targetGroupId;
                    RefreshSubGroupBar();
                }
            }
            catch (Exception ex)
            {
                LogUtil.WriteErrorLog(ex, "图标移入分组失败!");
            }
        }

        /// <summary>
        /// 把已有图标移入指定分组(或默认列表)  并刷新显示
        /// </summary>
        /// <param name="menu"></param>
        /// <param name="icon"></param>
        /// <param name="targetGroupId">null=默认列表</param>
        private void MoveIconToGroup(MenuInfo menu, IconInfo icon, string targetGroupId)
        {
            if (menu == null || icon == null) return;
            //图标当前实际所在的分组(归属失效时按默认列表算)
            string currentGroupId = GetSubGroup(menu, icon.SubGroupId) == null ? null : icon.SubGroupId;
            if (string.Equals(currentGroupId, targetGroupId)) return; //已在目标组
            //从来源列表移除(默认列表或某个分组)  并清除归属
            RemoveIconFromMenu(menu, icon);
            icon.SubGroupId_NoWrite = targetGroupId;
            GetTargetList(menu, targetGroupId).Add(icon);
            CommonCode.SaveAppData(MainWindow.appData, Constants.DATA_FILE_PATH);
            //当前查看的列表不是目标列表时  同步一次显示与标签行
            if (appData.AppConfig.SelectedMenuIcons != GetTargetList(menu, targetGroupId))
            {
                RefreshSubGroupBar();
            }
        }

        /// <summary>
        /// 右键"移动到分组"子菜单展开时  动态生成分组列表
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MoveToSubGroup_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            MenuItem moveToItem = sender as MenuItem;
            moveToMenuIcon = moveToItem.Tag as IconInfo;
            moveToItem.Items.Clear();
            MenuInfo menu = GetSelectedMenu();
            if (menu == null || moveToMenuIcon == null) return;

            MenuItem defaultItem = new MenuItem { Header = "默认列表" };
            defaultItem.Tag = moveToMenuIcon;
            defaultItem.Click += MoveToGroup_Click;
            defaultItem.IsEnabled = !string.IsNullOrEmpty(moveToMenuIcon.SubGroupId);
            moveToItem.Items.Add(defaultItem);

            foreach (SubGroupInfo sg in menu.SubGroups)
            {
                MenuItem groupItem = new MenuItem { Header = sg.SubName };
                groupItem.Tag = moveToMenuIcon;
                groupItem.Click += MoveToGroup_Click;
                //已经在该组的话置灰
                groupItem.IsEnabled = sg.SubId == null || !sg.SubId.Equals(moveToMenuIcon.SubGroupId);
                moveToItem.Items.Add(groupItem);
            }
            if (menu.SubGroups.Count == 0)
            {
                MenuItem emptyItem = new MenuItem { Header = "暂无分组, 请先点击 ＋ 新建", IsEnabled = false };
                moveToItem.Items.Add(emptyItem);
            }
        }

        /// <summary>
        /// 右键菜单选择分组  执行移动
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MoveToGroup_Click(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            IconInfo icon = mi.Tag as IconInfo;
            //Tag上只能拿到图标  通过Header找到对应分组
            MenuInfo menu = GetSelectedMenu();
            if (menu == null || icon == null) return;
            string targetGroupId = null;
            if (!"默认列表".Equals(mi.Header.ToString()))
            {
                foreach (SubGroupInfo sg in menu.SubGroups)
                {
                    if (sg.SubName.Equals(mi.Header.ToString()))
                    {
                        targetGroupId = sg.SubId;
                        break;
                    }
                }
            }
            MoveIconToGroup(menu, icon, targetGroupId);
            //移动后若当前显示列表不含该图标  切回包含它的列表  保持"看得见"的直觉
            ObservableCollection<IconInfo> targetList = GetTargetList(menu, targetGroupId);
            if (appData.AppConfig.SelectedMenuIcons != targetList)
            {
                appData.AppConfig.SelectedSubGroupId_NoWrite = targetGroupId;
                appData.AppConfig.SelectedMenuIcons = targetList;
                RefreshSubGroupBar();
            }
        }

        #endregion 二级分组



        //#region 图标拖动
        //DelegateCommand<int[]> _swap;
        //public DelegateCommand<int[]> SwapCommand
        //{
        //    get
        //    {
        //        if (_swap == null)
        //            _swap = new DelegateCommand<int[]>(
        //                (indexes) =>
        //                {
        //                    DROP_ICON = true;
        //                    if (appData.AppConfig.IconSortType != SortType.CUSTOM
        //                    && (dropCheckThread == null || !dropCheckThread.IsAlive))
        //                    {
        //                        dropCheckThread = new Thread(() =>
        //                        {
        //                            do
        //                            {
        //                                DROP_ICON = false;
        //                                Thread.Sleep(1000);
        //                            } while (DROP_ICON);

        //                            MainWindow.appData.AppConfig.IconSortType = SortType.CUSTOM;
        //                            App.Current.Dispatcher.Invoke(() =>
        //                            {
        //                                if (MainWindow.mainWindow.Visibility == Visibility.Collapsed
        //                                || MainWindow.mainWindow.Opacity != 1)
        //                                {
        //                                    Growl.WarningGlobal("已将图标排序规则重置为自定义!");
        //                                }
        //                                else
        //                                {
        //                                    Growl.Warning("已将图标排序规则重置为自定义!", "MainWindowGrowl");
        //                                }
        //                            });
        //                        });
        //                        dropCheckThread.Start();
        //                    }
        //                    int fromS = indexes[0];
        //                    int to = indexes[1];
        //                    ObservableCollection<IconInfo> iconList = appData.MenuList[appData.AppConfig.SelectedMenuIndex].IconList;
        //                    var elementSource = iconList[to];
        //                    var dragged = iconList[fromS];

        //                    iconList.Remove(dragged);
        //                    iconList.Insert(to, dragged);
        //                }
        //            );
        //        return _swap;
        //    }
        //}

        //#endregion 图标拖动




        private void Icon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            //开始点击/拖拽图标时立刻关闭路径提示  避免拖拽到桌面后提示残留不消失
            ClosePoptip();
            if (appData.AppConfig.IconBatch_NoWrite)
            {
                
                return;
            }
            if (appData.AppConfig.DoubleOpen)
            {
                IconClick(sender, e);
            }
        }

        private void Icon_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            //Console.WriteLine("选中:" + IconListBox.SelectedItems.Count);
            if (appData.AppConfig.IconBatch_NoWrite)
            {
                //查找checkbox更改选中状态
                Panel p = sender as Panel;
                var ens = p.Children.OfType<CheckBox>();
                foreach (CheckBox cb in ens)
                {
                    cb.IsChecked = !cb.IsChecked;
                }
                return;
            }
            if (!appData.AppConfig.DoubleOpen)
            {
                IconClick(sender, e);
            }
        }

        /// <summary>
        /// 图标点击事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void IconClick(object sender, MouseButtonEventArgs e)
        {
            if (!RunTimeStatus.SEARCH_BOX_HIDED_300) return;

            if (appData.AppConfig.DoubleOpen && e.ClickCount >= 2)
            {
                IconInfo icon = (IconInfo)((Panel)sender).Tag;
                if (icon.AdminStartUp)
                {
                    ProcessUtil.StartIconApp(icon, IconStartType.ADMIN_STARTUP);
                }
                else
                {
                    ProcessUtil.StartIconApp(icon, IconStartType.DEFAULT_STARTUP);
                }
            }
            else if (!appData.AppConfig.DoubleOpen && e.ClickCount == 1)
            {
                IconInfo icon = (IconInfo)((Panel)sender).Tag;
                if (icon.AdminStartUp)
                {
                    ProcessUtil.StartIconApp(icon, IconStartType.ADMIN_STARTUP);
                }
                else
                {
                    ProcessUtil.StartIconApp(icon, IconStartType.DEFAULT_STARTUP);
                }
            }

        }

        /// <summary>
        /// 管理员方式启动
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void IconAdminStart(object sender, RoutedEventArgs e)
        {
            IconInfo icon = (IconInfo)((MenuItem)sender).Tag;
            ProcessUtil.StartIconApp(icon, IconStartType.ADMIN_STARTUP);
        }

        /// <summary>
        /// 打开文件所在位置
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ShowInExplore(object sender, RoutedEventArgs e)
        {
            IconInfo icon = (IconInfo)((MenuItem)sender).Tag;
            ProcessUtil.StartIconApp(icon, IconStartType.SHOW_IN_EXPLORE);
        }

        /// <summary>
        /// 拖动添加项目  新图标进入当前显示的列表(默认或分组)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Wrap_Drop(object sender, DragEventArgs e)
        {
            //外部拖拽结束  关闭可能残留的提示框
            ClosePoptip();
            Array dropObject = (System.Array)e.Data.GetData(DataFormats.FileDrop);
            if (dropObject == null) return;
            foreach (object obj in dropObject)
            {
                string path = (string)obj;
                IconInfo iconInfo = CommonCode.GetIconInfoByPath(path);
                if (iconInfo == null)
                {
                    LogUtil.WriteErrorLog("添加项目失败，未能获取到项目图标:" + path);
                    continue;
                }
                //统一入口: 进入当前显示的列表(默认或当前分组)  分组ID经过有效性校验
                AddIconToCurrentList(iconInfo);
            }
            CommonCode.SortIconList();
            CommonCode.SaveAppData(MainWindow.appData, Constants.DATA_FILE_PATH);
        }

        /// <summary>
        /// 从列表删除图标
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RemoveIcon(object sender, RoutedEventArgs e)
        {
            IconInfo icon = (IconInfo)((MenuItem)sender).Tag;
            if (icon == null) return;
            //使用真实选中的菜单  避免配置索引滞后时删到其它菜单
            MenuInfo menu = GetSelectedMenu();
            if (menu == null) return;
            RemoveIconFromMenu(menu, icon);
            CommonCode.SaveAppData(MainWindow.appData, Constants.DATA_FILE_PATH);
            RefreshSubGroupBar();
        }

        private void SystemContextMenu(object sender, RoutedEventArgs e)
        {
            IconInfo icon = (IconInfo)((MenuItem)sender).Tag;
            DirectoryInfo[] folders = new DirectoryInfo[1];
            folders[0] = new DirectoryInfo(icon.Path);
            ShellContextMenu scm = new ShellContextMenu();
            System.Drawing.Point p = System.Windows.Forms.Cursor.Position;
            p.X -= 80;
            p.Y -= 80;
            scm.ShowContextMenu(folders, p);
        }

        /// <summary>
        /// 弹出Icon属性修改面板
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PropertyConfig(object sender, RoutedEventArgs e)
        {
            IconInfo info = (IconInfo)((MenuItem)sender).Tag;
            switch (info.IconType)
            {
                case IconType.URL:
                    IconInfoUrlDialog urlDialog = new IconInfoUrlDialog(info);
                    urlDialog.dialog = HandyControl.Controls.Dialog.Show(urlDialog, "MainWindowDialog");
                    break;
                default:
                    IconInfoDialog dialog = new IconInfoDialog(info);
                    dialog.dialog = HandyControl.Controls.Dialog.Show(dialog, "MainWindowDialog");
                    break;
            }
        }

        /// <summary>
        /// 关闭悬浮提示  并作废尚未执行的延迟打开请求
        /// 修复: 鼠标快速移出/开始拖拽后提示框仍留在桌面不消失的问题
        /// </summary>
        public void ClosePoptip()
        {
            poptipRequestId++;
            if (MyPoptip.IsOpen)
            {
                MyPoptip.IsOpen = false;
            }
        }

        /// <summary>
        /// 延迟显示悬浮提示  延时期间鼠标移出、开始拖拽、滚动列表都会使本次请求失效
        /// </summary>
        /// <param name="panel">图标面板</param>
        /// <param name="requestId">请求序号</param>
        private void ShowPoptipLater(Panel panel, int requestId)
        {
            ThreadPool.QueueUserWorkItem(state =>
            {
                //在后台线程等待  不能在UI线程Sleep(旧实现会阻塞界面)
                Thread.Sleep(50);
                try
                {
                    this.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (requestId != poptipRequestId) return;               //已被取消(鼠标移出或开始拖拽)
                        if (panel == null || !panel.IsMouseOver) return;         //鼠标已不在该图标上
                        if (!RunTimeStatus.MOUSE_ENTER_ICON
                            || RunTimeStatus.ICONLIST_MOUSE_WHEEL) return;      //正在滚动列表
                        IconInfo info = panel.Tag as IconInfo;
                        if (info == null) return;
                        MyPoptipContent.Text = info.Content;
                        MyPoptip.VerticalOffset = 30;
                        MyPoptip.IsOpen = true;
                    }));
                }
                catch (Exception) { }
            });
        }

        private void MenuIcon_MouseEnter(object sender, MouseEventArgs e)
        {
            RunTimeStatus.MOUSE_ENTER_ICON = true;
            Panel panel = sender as Panel;
            if (panel == null) return;
            if (!RunTimeStatus.ICONLIST_MOUSE_WHEEL)
            {
                //延迟显示图标路径提示(延时期间鼠标移出即取消)
                ShowPoptipLater(panel, ++poptipRequestId);
            }

            double width = appData.AppConfig.ImageWidth;
            double height = appData.AppConfig.ImageHeight;
            width += width * 0.15;
            height += height * 0.15;

            ThreadPool.QueueUserWorkItem(state =>
            {
                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    ImgStoryBoard(sender, (int)width, (int)height, 1, true);
                }));
            });
        }

        private void MenuIcon_MouseLeave(object sender, MouseEventArgs e)
        {
            RunTimeStatus.MOUSE_ENTER_ICON = false;
            ClosePoptip();

            ThreadPool.QueueUserWorkItem(state =>
            {
                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    ImgStoryBoard(sender, appData.AppConfig.ImageWidth, appData.AppConfig.ImageHeight, 260);
                }));
            });
        }


        private void ImgStoryBoard(object sender, int height, int width, int milliseconds, bool checkRmStoryboard = false)
        {

            if (appData.AppConfig.PMModel) return;

            //int count = 0;
            //Panel sp = sender as Panel;
            //Image img = sp.Children[0] as Image;


            //int nowH = (int)img.Height;

            //bool isSmall = nowH > height;

            //if (!isSmall)
            //{
            //    img.Height = height;
            //    img.Width = width;
            //    return;
            //}
            //double subLen = (double)Math.Abs(nowH - height) / (double)milliseconds;

            //new Thread(() =>
            //{
            //    this.Dispatcher.Invoke(() =>
            //    {
            //        while (count < milliseconds)
            //        {
            //            if (!isSmall)
            //            {
            //                img.Height += subLen;
            //                img.Width += subLen;
            //            } else
            //            {
            //                //if (img.Height > 1)
            //                //{
            //                //    img.Height -= 1;
            //                //    img.Width -= 1;
            //                //}
            //                Application.Current.Dispatcher.Invoke(DispatcherPriority.Background,
            //                             new Action(delegate {
            //                                 img.Height -= subLen;
            //                                 img.Width -= subLen;
            //                             }));
            //                //img.Height -= subLen;
            //                //img.Width -= subLen;
            //            }
            //            count++;
            //            Thread.Sleep(1);
            //        }
            //        img.Height = height;
            //        img.Width = width;
            //    });
            //}).Start();




            Panel sp = sender as Panel;

            DependencyObject dos = sp.Parent;
            Image img = null;

            foreach (var imgBak in sp.Children.OfType<Image>())
            {
                img = (Image)imgBak;

            }
            if (img == null) return;
            double afterHeight = img.Height;
            double afterWidth = img.Width;

            //动画定义
            Storyboard myStoryboard = new Storyboard();



            DoubleAnimation heightAnimation = new DoubleAnimation
            {
                From = afterHeight,
                To = height,
                Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds))
            };
            DoubleAnimation widthAnimation = new DoubleAnimation
            {
                From = afterWidth,
                To = width,
                Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds))
            };


            Timeline.SetDesiredFrameRate(heightAnimation, 60);
            Timeline.SetDesiredFrameRate(widthAnimation, 60);

            Storyboard.SetTarget(widthAnimation, img);
            Storyboard.SetTargetProperty(widthAnimation, new PropertyPath("Width"));
            Storyboard.SetTarget(heightAnimation, img);
            Storyboard.SetTargetProperty(heightAnimation, new PropertyPath("Height"));

            myStoryboard.Children.Add(heightAnimation);
            myStoryboard.Children.Add(widthAnimation);

            CheckRemoveStoryboard crs = new CheckRemoveStoryboard
            {
                sb = myStoryboard,
                sp = sp,
                heightAnimation = heightAnimation,
                widthAnimation = widthAnimation,
                img = img,
                isMouseOver = !checkRmStoryboard
            };

            heightAnimation.Completed += (s, e) =>
            {
                if (checkRmStoryboard)
                {
                    ThreadStart ts = new ThreadStart(crs.Remove);
                    System.Threading.Thread t = new System.Threading.Thread(ts);
                    t.IsBackground = true;
                    t.Start();
                }
                else
                {
                    img.BeginAnimation(WidthProperty, null);
                    img.BeginAnimation(HeightProperty, null);
                }
            };
            img.BeginAnimation(WidthProperty, widthAnimation);
            img.BeginAnimation(HeightProperty, heightAnimation);
            //###################################################################
            //myStoryboard.Completed += (s, e) =>
            //{
            //    if (checkRmStoryboard)
            //    {
            //        ThreadStart ts = new ThreadStart(crs.Remove);
            //        System.Threading.Thread t = new System.Threading.Thread(ts);
            //        t.Start();
            //    }
            //    else
            //    {
            //        myStoryboard.Remove();
            //    }
            //};
            //myStoryboard.Begin();
        }

        private class CheckRemoveStoryboard
        {
            public Storyboard sb;
            public Panel sp;
            public Image img;
            public DoubleAnimation heightAnimation;
            public DoubleAnimation widthAnimation;
            public bool isMouseOver;
            public void Remove()
            {
                while (true)
                {
                    if (sp.IsMouseOver == isMouseOver)
                    {
                        App.Current.Dispatcher.Invoke((Action)(() =>
                        {
                            img.BeginAnimation(WidthProperty, null);
                            img.BeginAnimation(HeightProperty, null);
                            //heightAnimation.FillBehavior = FillBehavior.Stop;
                            //widthAnimation.FillBehavior = FillBehavior.Stop;
                        }));
                        return;
                    }
                    else
                    {
                        System.Threading.Thread.Sleep(500);
                    }
                }
            }
        }

        public void RemoveSB(Object sb)
        {
            Storyboard sb2 = sb as Storyboard;
            System.Threading.Thread.Sleep(500);
            sb2.Remove();
        }

        /// <summary>
        /// 添加URL项目
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AddUrlIcon(object sender, RoutedEventArgs e)
        {
            IconInfoUrlDialog urlDialog = new IconInfoUrlDialog();
            urlDialog.dialog = HandyControl.Controls.Dialog.Show(urlDialog, "MainWindowDialog");
        }

        /// <summary>
        /// 添加系统项目
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AddSystemIcon(object sender, RoutedEventArgs e)
        {
            SystemItemWindow.Show();
        }

        public void VisibilitySearchCard(Visibility vb)
        {
            VerticalCard.Visibility = vb;
            if (vb == Visibility.Visible)
            {
                WrapCard.Visibility = Visibility.Collapsed;
            }
            else
            {
                WrapCard.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// 搜索Card点击事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void VerticalCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            //隐藏搜索框
            if (RunTimeStatus.SEARCH_BOX_SHOW)
            {
                MainWindow.mainWindow.HidedSearchBox();
            }
        }

        /// <summary>
        /// 设置光标
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CursorPanel_MouseEnter(object sender, MouseEventArgs e)
        {
            this.Cursor = Cursors.Hand;
        }

        /// <summary>
        /// 设置光标
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CursorPanel_MouseLeave(object sender, MouseEventArgs e)
        {
            this.Cursor = Cursors.Arrow;
        }

        /// <summary>
        /// 锁定/解锁主面板
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LockAppPanel(object sender, RoutedEventArgs e)
        {
            RunTimeStatus.LOCK_APP_PANEL = !RunTimeStatus.LOCK_APP_PANEL;
        }

        private void WrapCard_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (RunTimeStatus.LOCK_APP_PANEL)
            {
                CardLockCM.Header = "解锁主面板";
            }
            else
            {
                CardLockCM.Header = "锁定主面板";
            }
            //关联菜单不支持分组  隐藏新建分组入口
            MenuInfo menu = GetSelectedMenu();
            if (CardAddSubGroupCM != null)
            {
                CardAddSubGroupCM.Visibility = menu == null || menu.MenuType == MenuType.LINK
                    ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        

        private void PDDialog_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (PDDialog.Visibility == Visibility.Visible)
            {
                RunTimeStatus.SHOW_MENU_PASSWORDBOX = true;
                PDDialog.ClearVal();
                PDDialog.ErrorMsg.Visibility = Visibility.Collapsed;
                PDDialog.PasswordGrid.Visibility = Visibility.Visible;
                PDDialog.HintGrid.Visibility = Visibility.Collapsed;
                PDDialog.count = 0;
                PDDialog.SetFocus();
            }
            else
            {
                RunTimeStatus.SHOW_MENU_PASSWORDBOX = false;
                PDDialog.ClearVal();
                MainWindow.mainWindow.Focus();
            }
        }

        /// <summary>
        /// 菜单结果icon 列表鼠标滚轮预处理时间  
        /// 主要使用自定义popup解决卡顿问题解决卡顿问题
        /// 以及滚动条首尾切换菜单
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void IconListBox_MouseWheel(object sender, MouseWheelEventArgs e)
        {

            //控制在滚动时不显示popup 否则会在低GPU性能机器上造成卡顿
            MyPoptip.IsOpen = false;
            if (RunTimeStatus.ICONLIST_MOUSE_WHEEL)
            {
                RunTimeStatus.MOUSE_WHEEL_WAIT_MS = 500;
            }
            else
            {
                RunTimeStatus.ICONLIST_MOUSE_WHEEL = true;

                new Thread(() =>
                {
                    while (RunTimeStatus.MOUSE_WHEEL_WAIT_MS > 0)
                    {
                        Thread.Sleep(1);
                        RunTimeStatus.MOUSE_WHEEL_WAIT_MS -= 1;
                    }
                    if (RunTimeStatus.MOUSE_ENTER_ICON && IconListBox.IsMouseOver)
                    {
                        this.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (RunTimeStatus.MOUSE_ENTER_ICON && IconListBox.IsMouseOver)
                            {
                                MyPoptip.IsOpen = true;
                            }
                        }));
                    }
                    RunTimeStatus.MOUSE_WHEEL_WAIT_MS = 100;
                    RunTimeStatus.ICONLIST_MOUSE_WHEEL = false;
                }).Start();
            }

            //修改菜单时不切换菜单
            if (RunTimeStatus.IS_MENU_EDIT) return;


            //切换菜单
            System.Windows.Controls.ScrollViewer scrollViewer = sender as System.Windows.Controls.ScrollViewer;
            if (scrollViewer == null)
            {
                //在card 上获取的事件
                scrollViewer = ScrollUtil.FindSimpleVisualChild<System.Windows.Controls.ScrollViewer>(IconListBox);
            }
            if (e.Delta < 0)
            {
                int index = MainWindow.mainWindow.LeftCard.MenuListBox.SelectedIndex;
                if (ScrollUtil.IsBootomScrollView(scrollViewer))
                {
                    if (index < MainWindow.mainWindow.LeftCard.MenuListBox.Items.Count - 1)
                    {
                        index++;
                    }
                    else
                    {
                        index = 0;
                    }
                    MainWindow.mainWindow.LeftCard.MenuListBox.SelectedIndex = index;
                    scrollViewer.ScrollToVerticalOffset(0);
                }
            }
            else if (e.Delta > 0)
            {
                if (ScrollUtil.IsTopScrollView(scrollViewer))
                {
                    int index = MainWindow.mainWindow.LeftCard.MenuListBox.SelectedIndex;
                    if (index > 0)
                    {
                        index--;
                    }
                    else
                    {
                        index = MainWindow.mainWindow.LeftCard.MenuListBox.Items.Count - 1;
                    }
                    MainWindow.mainWindow.LeftCard.MenuListBox.SelectedIndex = index;
                    scrollViewer.ScrollToVerticalOffset(0);
                }
            }


        }

        /// <summary>
        /// menu结果ICON鼠标移动事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MenuIcon_MouseMove(object sender, MouseEventArgs e)
        {
            //防止移动后不刷新popup content
            IconInfo info = (sender as Panel).Tag as IconInfo;
            if (info == null) return;
            MyPoptipContent.Text = info.Content;
            MyPoptip.VerticalOffset = 30;
        }

        /// <summary>
        /// 控制图标标题显示及隐藏
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ShowTitle_Click(object sender, RoutedEventArgs e)
        {
            appData.AppConfig.ShowIconTitle = !appData.AppConfig.ShowIconTitle;
        }

        /// <summary>
        /// 批量操作
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BatchHandle(object sender, RoutedEventArgs e)
        {
            if (!appData.AppConfig.IconBatch_NoWrite)
            {
                //开启批量操作时把所有的状态更改为未选中
               foreach(var ic in IconListBox.Items)
                {
                    IconInfo info = ic as IconInfo;
                    info.IsChecked_NoWrite = false;
                }
            }
            appData.AppConfig.IconBatch_NoWrite = !appData.AppConfig.IconBatch_NoWrite;
            IconListBox.SelectionMode = SelectionMode.Multiple;
        }

        private void IconListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //Console.WriteLine(sender.ToString()); 
        }
    }
}
