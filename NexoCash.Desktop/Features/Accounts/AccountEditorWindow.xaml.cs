using System.Windows;
using NexoCash.Domain.Entities;
using NexoCash.Infrastructure.Persistence;

namespace NexoCash.Desktop.Features.Accounts;

public partial class AccountEditorWindow : Window
{
    private readonly AccountRepository _repository = new();
    private readonly Guid? _id;
    public Guid? SavedId { get; private set; }

    public AccountEditorWindow(AccountListItem? item)
    {
        InitializeComponent();
        if (item is null) return;
        _id = item.Account.Id;
        Title = FormTitle.Text = "Editar cuenta";
        NameBox.Text = item.Account.Name;
        TypePicker.SelectedIndex = item.Account.Type == AccountType.Cash ? 0 : 1;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SavedId = _repository.Save(_id, NameBox.Text,
                TypePicker.SelectedIndex == 0 ? AccountType.Cash : AccountType.Virtual);
            DialogResult = true;
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
