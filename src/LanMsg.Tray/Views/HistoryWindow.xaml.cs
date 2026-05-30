using System.Windows;
using LanMsg.Tray.History;

namespace LanMsg.Tray.Views;

public partial class HistoryWindow : Window
{
    public HistoryWindow(HistoryStore history)
    {
        InitializeComponent();
        HistoryList.ItemsSource = history.GetAll().Select(h => new
        {
            Time = h.Timestamp.ToLocalTime().ToString("g"),
            h.Direction,
            From = $"{h.SenderName} ({h.Hostname})",
            h.Body
        }).ToList();
    }
}
