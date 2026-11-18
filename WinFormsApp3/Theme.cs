using System.Drawing;

namespace SkyWings;

/// <summary>Colors, fonts and spacing from the Java version (UI constants).</summary>
public static class Theme
{
    // ===== COLOR SCHEME =====
    public static readonly Color Primary = Color.FromArgb(0x52, 0x78, 0x66);
    public static readonly Color PrimaryLight = Color.FromArgb(0x69, 0x92, 0x80);
    public static readonly Color Accent = Color.FromArgb(0x31, 0x5B, 0x49);
    public static readonly Color AccentDark = Color.FromArgb(0x23, 0x47, 0x39);
    public static readonly Color SoftFill = Color.FromArgb(0xDA, 0xEC, 0xE1);
    public static readonly Color AccentLight = Color.FromArgb(0xF3, 0xF7, 0xF4);
    public static readonly Color BgMain = AccentLight;
    public static readonly Color BgCard = Color.White;
    public static readonly Color TextPrimary = Color.FromArgb(0x17, 0x1C, 0x1A);
    public static readonly Color TextSecondary = Color.FromArgb(0x77, 0x85, 0x7E);
    public static readonly Color SuccessGreen = Color.FromArgb(48, 209, 88);
    public static readonly Color DangerRed = Color.FromArgb(255, 69, 58);
    public static readonly Color Border = Color.FromArgb(0xE1, 0xE8, 0xE4);
    public static readonly Color BorderLight = Color.FromArgb(0xDF, 0xE7, 0xE1);
    public static readonly Color StepActive = Primary;
    public static readonly Color StepDone = Accent;
    public static readonly Color StepPending = Color.FromArgb(0xE8, 0xF0, 0xEB);
    public static readonly Color BgMuted = Color.FromArgb(0xF5, 0xF8, 0xF6);
    public static readonly Color Warning = Color.FromArgb(176, 128, 20);
    public static readonly Color Selection = Color.FromArgb(226, 239, 232);
    public static readonly Color RowAlt = Color.FromArgb(250, 252, 251);
    public static readonly Color DisabledText = Color.FromArgb(160, 172, 166);
    public static readonly Color DisabledTextSoft = Color.FromArgb(176, 187, 181);
    public static readonly Color DisabledBack = Color.FromArgb(240, 244, 242);

    // ===== FONTS =====
    // Java sizes are in pixels; WinForms uses points (pt = px * 0.75).
    public static readonly Font Title = new("Segoe UI", 18f, FontStyle.Bold);
    public static readonly Font Subtitle = new("Segoe UI", 13.5f, FontStyle.Bold);
    public static readonly Font Header = new("Segoe UI", 15f, FontStyle.Bold);
    public static readonly Font Body = new("Segoe UI", 11f, FontStyle.Regular);
    public static readonly Font Small = new("Segoe UI", 9.75f, FontStyle.Regular);
    public static readonly Font Mono = new("Consolas", 10.5f, FontStyle.Regular);

    // ===== SPACING SCALE =====
    public const int Sp6 = 6, Sp10 = 10, Sp12 = 12, Sp16 = 16, Sp20 = 20, Sp24 = 24, Sp30 = 30;

    /// <summary>
    /// Applies the default look to a control tree. Controls that style themselves (buttons, cards, seat map,
    /// fields...) are skipped. Panels and labels inherit the colour of the surface they sit on; transparency is
    /// removed because it makes every repaint cascade through the parent chain.
    /// </summary>
    public static void Apply(Control control)
    {
        if (control is SkyWings.UI.ISelfStyled) return;

        if (control is Form or TabPage) control.BackColor = BgMain;
        else if (control is Panel or Label or CheckBox or RadioButton && control.BackColor == Color.Transparent)
            control.ResetBackColor();   // fall back to the parent's colour (ambient BackColor)
        control.ForeColor = TextPrimary;

        // Only labels still on the default 9pt font get the body font; explicit fonts are kept.
        if (control is Label label && Math.Abs(label.Font.Size - 9f) < 0.01f) label.Font = Body;
        if (control is TextBox or ComboBox or DateTimePicker or NumericUpDown or ListBox or CheckedListBox)
        {
            control.Font = Body;
            control.BackColor = BgCard;
        }
        if (control is Button button)
        {
            button.Font = Body;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Primary;
            button.ForeColor = Color.White;
            button.Padding = new Padding(12, 6, 12, 6);
        }

        foreach (Control child in control.Controls) Apply(child);
    }
}
