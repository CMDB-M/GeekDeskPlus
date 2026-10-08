using GeekDesk.Constant;
using GeekDesk.Util;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

/// <summary>
/// 菜单内二级分组信息
/// </summary>
namespace GeekDesk.ViewModel
{
    [Serializable]
    public class SubGroupInfo : INotifyPropertyChanged
    {
        private string subName; //分组名称
        private string subId; //分组唯一ID
        private ObservableCollection<IconInfo> iconList; //组内图标列表

        public string SubName
        {
            get
            {
                return subName;
            }
            set
            {
                subName = value;
                OnPropertyChanged("SubName");
            }
        }

        public string SubId
        {
            get
            {
                return subId;
            }
            set
            {
                subId = value;
                OnPropertyChanged("SubId");
            }
        }

        public ObservableCollection<IconInfo> IconList
        {
            get
            {
                if (iconList == null)
                {
                    iconList = new ObservableCollection<IconInfo>();
                }
                return iconList;
            }
            set
            {
                iconList = value;
                OnPropertyChanged("IconList");
            }
        }

        [field: NonSerializedAttribute()]
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            CommonCode.SaveAppData(MainWindow.appData, Constants.DATA_FILE_PATH);
        }
    }
}
