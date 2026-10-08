using GeekDesk.Constant;
using GeekDesk.Control.Other;
using GeekDesk.ViewModel;
using HandyControl.Data;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Windows;
using System.Windows.Media.Imaging;
using static GeekDesk.Control.Other.GlobalMsgNotification;

/// <summary>
/// 提取一些代码
/// </summary>
namespace GeekDesk.Util
{
    class CommonCode
    {

        /// <summary>
        /// 获取app 数据
        /// </summary>
        /// <returns></returns>
        public static AppData GetAppDataByFile()
        {
            AppData appData;
            if (!File.Exists(Constants.DATA_FILE_PATH))
            {
                using (FileStream fs = File.Create(Constants.DATA_FILE_PATH)) { }
                appData = new AppData();
                SaveAppData(appData, Constants.DATA_FILE_PATH);
                return appData;
            }
            else
            {
                try
                {
                    using (FileStream fs = new FileStream(Constants.DATA_FILE_PATH, FileMode.Open))
                    {
                        BinaryFormatter bf = new BinaryFormatter();
                        appData = bf.Deserialize(fs) as AppData;

                        //将菜单密码写入文件
                        if (!string.IsNullOrEmpty(appData.AppConfig.MenuPassword))
                        {
                            SavePassword(appData.AppConfig.MenuPassword);
                        }
                        return appData;
                    }
                }
                catch
                {
                    DirectoryInfo dirInfo = new DirectoryInfo(Constants.DATA_FILE_BAK_DIR_PATH);
                    FileInfo[] files = dirInfo.GetFiles()
                        .Where(f => f.Extension.Equals(".bak", StringComparison.OrdinalIgnoreCase)).ToArray(); ;
                    if (files.Length > 0)
                    {
                        FileInfo[] sortedFiles = files.OrderByDescending(file => file.CreationTime).ToArray();
                        //循环获取可用备份文件
                        string bakFilePath = "";
                        foreach (FileInfo bakFile in sortedFiles)
                        {
                            if (!Directory.Exists(Constants.DATA_FILE_TEMP_DIR_PATH)) { Directory.CreateDirectory(Constants.DATA_FILE_TEMP_DIR_PATH); }
                            bakFilePath = Constants.DATA_FILE_TEMP_DIR_PATH + "\\" + bakFile.Name;
                            try
                            {
                                File.Copy(bakFile.FullName, bakFilePath, true);
                                using (FileStream fs = new FileStream(bakFilePath, FileMode.Open))
                                {
                                    BinaryFormatter bf = new BinaryFormatter();
                                    appData = bf.Deserialize(fs) as AppData;
                                }
                                DialogMsg msg = new DialogMsg();
                                msg.msg = "不幸的是, GeekDesk当前的数据文件已经损坏, " +
                                    "现在已经启用系统自动备份的数据\n\n" +
                                    "如果你有较新的备份, " +
                                    "请退出GeekDesk, " +
                                    "将备份文件重命名为:Data, " +
                                    "然后将Data覆盖到GeekDesk的根目录即可\n\n" +
                                    "启用的备份文件为: \n" + bakFilePath +
                                    "\n\n如果当前数据就是你想要的数据, 那么请不用管它";
                                GlobalMsgNotification gm = new GlobalMsgNotification(msg);
                                HandyControl.Controls.Notification ntf = HandyControl.Controls.Notification.Show(gm, ShowAnimation.Fade, true);
                                gm.ntf = ntf;
                                File.Delete(bakFilePath);
                                SaveAppData(appData, Constants.DATA_FILE_PATH);
                                return appData;
                            }
                            catch { 
                                if (File.Exists(bakFilePath))
                                {
                                    File.Delete(bakFilePath);
                                }
                            }
                        }
                        MessageBox.Show("不幸的是, GeekDesk当前的数据文件已经损坏\n如果你有备份, 请将备份文件重命名为:Data 然后将Data覆盖到GeekDesk的根目录即可!");
                        Application.Current.Shutdown();
                        return new AppData();
                    } else
                    {
                        MessageBox.Show("不幸的是, GeekDesk当前的数据文件已经损坏\n如果你有备份, 请将备份文件重命名为:Data 然后将Data覆盖到GeekDesk的根目录即可!");
                        Application.Current.Shutdown();
                        return new AppData();
                    }

                    //    if (File.Exists(Constants.DATA_FILE_BAK_PATH))
                    //{
                    //    try
                    //    {
                    //        using (FileStream fs = new FileStream(Constants.DATA_FILE_BAK_PATH, FileMode.Open))
                    //        {
                    //            BinaryFormatter bf = new BinaryFormatter();
                    //            appData = bf.Deserialize(fs) as AppData;
                    //        }

                    //        DialogMsg msg = new DialogMsg();
                    //        msg.msg = "不幸的是, GeekDesk当前的数据文件已经损坏, " +
                    //            "现在已经启用系统自动备份的数据\n\n" +
                    //            "如果你有较新的备份, " +
                    //            "请退出GeekDesk, " +
                    //            "将备份文件重命名为:Data, " +
                    //            "然后将Data覆盖到GeekDesk的根目录即可\n\n" +
                    //            "系统上次备份时间: \n" + appData.AppConfig.SysBakTime +
                    //            "\n\n如果当前数据就是你想要的数据, 那么请不用管它";
                    //        GlobalMsgNotification gm = new GlobalMsgNotification(msg);
                    //        HandyControl.Controls.Notification ntf = HandyControl.Controls.Notification.Show(gm, ShowAnimation.Fade, true);
                    //        gm.ntf = ntf;
                    //    }
                    //    catch
                    //    {
                    //        MessageBox.Show("不幸的是, GeekDesk当前的数据文件已经损坏\n如果你有备份, 请将备份文件重命名为:Data 然后将Data覆盖到GeekDesk的根目录即可!");
                    //        Application.Current.Shutdown();
                    //        return null;
                    //    }

                    //}
                    //else
                    //{
                    //    MessageBox.Show("不幸的是, GeekDesk当前的数据文件已经损坏\n如果你有备份, 请将备份文件重命名为:Data 然后将Data覆盖到GeekDesk的根目录即可!");
                    //    Application.Current.Shutdown();
                    //    return null;
                    //}

                }
            }
        }

        private readonly static object _MyLock = new object();
        /// <summary>
        /// 保存app 数据
        /// </summary>
        /// <param name="appData"></param>
        public static void SaveAppData(AppData appData, string filePath)
        {
            lock (_MyLock)
            {
                //if (filePath.Equals(Constants.DATA_FILE_BAK_PATH))
                //{
                //    appData.AppConfig.SysBakTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                //}
                if (!Directory.Exists(filePath.Substring(0, filePath.LastIndexOf("\\"))))
                {
                    Directory.CreateDirectory(filePath.Substring(0, filePath.LastIndexOf("\\")));
                }
                using (FileStream fs = new FileStream(filePath, FileMode.Create))
                {
                    BinaryFormatter bf = new BinaryFormatter();
                    bf.Serialize(fs, appData);
                }
            }
        }


        public static void SavePassword(string password)
        {
            using (StreamWriter sw = new StreamWriter(Constants.PW_FILE_BAK_PATH))
            {
                sw.Write(password);
            }
        }

        private static string GeneraterUUID()
        {
            try
            {
                if (!File.Exists(Constants.UUID_FILE_BAK_PATH) || string.IsNullOrEmpty(GetUniqueUUID()))
                {
                    using (StreamWriter sw = new StreamWriter(Constants.UUID_FILE_BAK_PATH))
                    {
                        string uuid = Guid.NewGuid().ToString() + "-" + Constants.MY_UUID;
                        sw.Write(uuid);
                        return uuid;
                    }
                }
            } catch (Exception) { }
            return "ERROR_UUID_GeneraterUUID_" + Constants.MY_UUID;
        }

        public static string GetUniqueUUID()
        {
            try
            {
                if (File.Exists(Constants.UUID_FILE_BAK_PATH))
                {
                    using (StreamReader reader = new StreamReader(Constants.UUID_FILE_BAK_PATH))
                    {
                        return reader.ReadToEnd().Trim();
                    }
                } else
                {
                    return GeneraterUUID();
                }
            } catch(Exception) { }
            return "ERROR_UUID_GetUniqueUUID_" + Constants.MY_UUID;
        }


        public static void BakAppData()
        {

            SaveFileDialog sfd = new SaveFileDialog
            {
                Title = "备份文件",
                Filter = "bak文件(*.bak)|*.bak",
                FileName = "Data-GD-" + DateTime.Now.ToString("yyMMdd") + ".bak",
            };
            if (sfd.ShowDialog() == true)
            {
                using (FileStream fs = new FileStream(sfd.FileName, FileMode.Create))
                {
                    BinaryFormatter bf = new BinaryFormatter();
                    bf.Serialize(fs, MainWindow.appData);
                }
            }
        }



        /// <summary>
        /// 根据路径获取文件图标等信息
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static IconInfo GetIconInfoByPath(string path)
        {
            string tempPath = path;

            //string base64 = ImageUtil.FileImageToBase64(path, System.Drawing.Imaging.ImageFormat.Png);
            //string ext = "";
            //if (!ImageUtil.IsSystemItem(path))
            //{
            //    ext = System.IO.Path.GetExtension(path).ToLower();
            //}

            string iconPath;
            //if (".lnk".Equals(ext))
            //{

            string targetPath = FileUtil.GetTargetPathByLnk(path);
            iconPath = FileUtil.GetIconPathByLnk(path);
            if (targetPath != null)
            {
                path = targetPath;
            }
            //}
            if (StringUtil.IsEmpty(iconPath))
            {
                iconPath = path;
            }

            BitmapImage bi = ImageUtil.GetBitmapIconByPath(iconPath);
            IconInfo iconInfo = new IconInfo
            {
                Path_NoWrite = path,
                LnkPath_NoWrite = tempPath,
                BitmapImage_NoWrite = bi,
                StartArg_NoWrite = FileUtil.GetArgByLnk(tempPath)
            };
            iconInfo.DefaultImage_NoWrite = iconInfo.ImageByteArr;
            iconInfo.Name_NoWrite = System.IO.Path.GetFileNameWithoutExtension(tempPath);
            if (StringUtil.IsEmpty(iconInfo.Name))
            {
                iconInfo.Name_NoWrite = path;
            }
            string relativePath = FileUtil.MakeRelativePath(Constants.APP_DIR + "GeekDesk.exe", iconInfo.Path);
            if (!string.IsNullOrEmpty(relativePath) && !relativePath.Equals(iconInfo.Path))
            {
                iconInfo.RelativePath_NoWrite = relativePath;
            }
            return iconInfo;
        }


        public static IconInfo GetIconInfoByPath_NoWrite(string path)
        {
            string tempPath = path;

            //string base64 = ImageUtil.FileImageToBase64(path, System.Drawing.Imaging.ImageFormat.Png);
            string ext = "";
            if (!ImageUtil.IsSystemItem(path))
            {
                ext = System.IO.Path.GetExtension(path).ToLower();
            }

            string iconPath = null;
            if (".lnk".Equals(ext))
            {

                string targetPath = FileUtil.GetTargetPathByLnk(path);
                iconPath = FileUtil.GetIconPathByLnk(path);
                if (targetPath != null)
                {
                    path = targetPath;
                }
            }
            if (StringUtil.IsEmpty(iconPath))
            {
                iconPath = path;
            }

            BitmapImage bi = ImageUtil.GetBitmapIconByPath(iconPath);
            IconInfo iconInfo = new IconInfo
            {
                Path_NoWrite = path,
                LnkPath_NoWrite = tempPath,
                BitmapImage_NoWrite = bi,
                StartArg_NoWrite = FileUtil.GetArgByLnk(tempPath)
            };
            iconInfo.DefaultImage_NoWrite = iconInfo.ImageByteArr;
            iconInfo.Name = System.IO.Path.GetFileNameWithoutExtension(tempPath);
            if (StringUtil.IsEmpty(iconInfo.Name))
            {
                iconInfo.Name_NoWrite = path;
            }
            return iconInfo;
        }






        /// <summary>
        /// 按当前排序规则排序图标列表
        /// </summary>
        /// <param name="list"></param>
        private static void SortIconInfos(List<IconInfo> list)
        {
            switch (MainWindow.appData.AppConfig.IconSortType)
            {
                case SortType.COUNT_UP:
                    list.Sort((x, y) => x.Count.CompareTo(y.Count));
                    break;
                case SortType.COUNT_LOW:
                    list.Sort((x, y) => y.Count.CompareTo(x.Count));
                    break;
                case SortType.NAME_UP:
                    list.Sort((x, y) => x.Name.CompareTo(y.Name));
                    break;
                case SortType.NAME_LOW:
                    list.Sort((x, y) => y.Name.CompareTo(x.Name));
                    break;
            }
        }

        /// <summary>
        /// 排序图标  默认列表和各二级分组内部分别排序  不打乱分组归属  也不改变当前显示的分组
        /// </summary>
        public static void SortIconList(bool sort = true)
        {
            try
            {
                if (MainWindow.appData.AppConfig.IconSortType != SortType.CUSTOM && sort)
                {
                    ObservableCollection<MenuInfo> menuList = MainWindow.appData.MenuList;
                    List<IconInfo> list;
                    //记住当前显示的分组ID  排序后恢复到对应的新集合引用
                    string selectedGroupId = MainWindow.appData.AppConfig.SelectedSubGroupId_NoWrite;

                    foreach (MenuInfo menuInfo in menuList)
                    {
                        list = new List<IconInfo>(menuInfo.IconList);
                        SortIconInfos(list);
                        menuInfo.IconList = new ObservableCollection<IconInfo>(list);
                        //各二级分组内部分别排序
                        if (menuInfo.SubGroups != null)
                        {
                            foreach (SubGroupInfo sg in menuInfo.SubGroups)
                            {
                                if (sg == null || sg.IconList == null) continue;
                                list = new List<IconInfo>(sg.IconList);
                                SortIconInfos(list);
                                sg.IconList = new ObservableCollection<IconInfo>(list);
                            }
                        }
                    }

                    //排序会重建各列表引用  这里统一由右侧面板按"真实选中的菜单+分组"重新绑定显示
                    //(旧实现按 AppConfig.SelectedMenuIndex 恢复  该索引在切换菜单事件中是滞后值  会把显示串到上一个菜单)
                    MainWindow mainWindow = MainWindow.mainWindow;
                    bool uiReady = mainWindow != null && mainWindow.RightCard != null
                        && Application.Current != null && Application.Current.Dispatcher.CheckAccess();
                    if (uiReady)
                    {
                        mainWindow.RightCard.RefreshSubGroupBar();
                    }
                    else
                    {
                        //窗口尚未就绪或不在UI线程  退化为按配置索引恢复, 并请求UI线程刷新一次
                        int index = MainWindow.appData.AppConfig.SelectedMenuIndex;
                        if (index < 0 || index >= menuList.Count) index = 0;
                        MenuInfo curMenu = menuList[index];
                        ObservableCollection<IconInfo> curList = curMenu.IconList;
                        if (!string.IsNullOrEmpty(selectedGroupId) && curMenu.SubGroups != null)
                        {
                            foreach (SubGroupInfo sg in curMenu.SubGroups)
                            {
                                if (selectedGroupId.Equals(sg.SubId))
                                {
                                    curList = sg.IconList;
                                    break;
                                }
                            }
                        }
                        MainWindow.appData.AppConfig.SelectedMenuIcons = curList;
                        if (mainWindow != null && mainWindow.RightCard != null
                            && Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
                        {
                            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try { mainWindow.RightCard.RefreshSubGroupBar(); } catch (Exception) { }
                            }));
                        }
                    }
                }
            }
            catch (Exception) { }

        }



        /// <summary>
        /// 判断鼠标是否在窗口内
        /// </summary>
        /// <param name="window"></param>
        /// <returns></returns>
        public static bool MouseInWindow(Window window)
        {
            double windowHeight = window.Height;
            double windowWidth = window.Width;

            double windowTop = window.Top;
            double windowLeft = window.Left;

            //获取鼠标位置
            System.Windows.Point p = MouseUtil.GetMousePosition();
            double mouseX = p.X;
            double mouseY = p.Y;

            //鼠标不在窗口上
            if (mouseX < windowLeft || mouseX > windowLeft + windowWidth
                || mouseY < windowTop || mouseY > windowTop + windowHeight)
            {
                return false;
            }
            return true;
        }

    }
}
