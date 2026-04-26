namespace SkinnyB.Controls;

public partial class EditableCell : ContentView
{
    public event EventHandler? CellSelected;

    public static readonly BindableProperty ValueProperty =
        BindableProperty.Create(nameof(Value), typeof(int), typeof(EditableCell), 0,
            propertyChanged: OnValueChanged);

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public event EventHandler? ValueChanged;

    public EditableCell()
    {
        InitializeComponent();
        UpdateDisplay();
    }

    private static void OnValueChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((EditableCell)bindable).UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        string text = Value.ToString();
        string color = ValueToColor(Value);

        ValueLabel.Text = text;
        EditLabel.Text = text;
        ValueLabel.TextColor = Color.FromArgb(color);
        EditLabel.TextColor = Color.FromArgb(color);
    }

    private static string ValueToColor(int v) => v switch
    {
        0 or 1 => "#E24B4A",   // red
        2 or 3 => "#BA7517",   // amber
        4 or 5 => "#3B6D11",   // green
        _ => "#0F6E56",   // teal (6-7)
    };

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        NormalView.IsVisible = false;
        EditView.IsVisible = true;
        CellSelected?.Invoke(this, e);
    }


    private void OnIncrement(object? sender, EventArgs e)
    {
        if (Value < 7) Value++;
    }

    private void OnDecrement(object? sender, EventArgs e)
    {
        if (Value > 0) Value--;
    }

    public void Deselect()
    {
        NormalView.IsVisible = true;
        EditView.IsVisible = false;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }
}