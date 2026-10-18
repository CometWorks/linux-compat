using System;
using System.Collections.Generic;
using System.Reflection;
using ClientPlugin.Settings.Tools;
using Sandbox.Graphics.GUI;
using Sandbox.Graphics.Gui;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.Settings.Elements;

internal class ColorAttribute : Attribute, IElement
{
    public readonly bool HasAlpha;
    public readonly string Label;
    public readonly string Description;

    private Color originalBorderColor;

    // MyGuiScreenDialogColor always starts at Color.White and exposes no way to seed
    // the initial color, so the hosted MyGuiControlColor has to be reached directly.
    // Sandbox.Graphics is not publicized, hence the reflection.
    private static readonly FieldInfo DialogColorControlField =
        typeof(MyGuiScreenDialogColor).GetField(
            "m_color",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

    public ColorAttribute(bool hasAlpha = false, string label = null, string description = null)
    {
        HasAlpha = hasAlpha;
        Label = label;
        Description = description;
    }

    public List<Control> GetControls(
        string name,
        Func<object> propertyGetter,
        Action<object> propertySetter
    )
    {
        var defaultColor = (Color)propertyGetter();
        var defaultColorHex = HasAlpha
            ? defaultColor.ToHexStringRgba()
            : defaultColor.ToHexStringRgb();

        var sample = new MyGuiControlButton(visualStyle: MyGuiControlButtonStyleEnum.SquareSmall)
        {
            BorderColor = defaultColor,
            BorderEnabled = true,
            BorderSize = 20,
        };

        var textBox = new MyGuiControlTextbox(
            defaultText: defaultColorHex,
            maxLength: HasAlpha ? 8 : 6
        )
        {
            Size = new Vector2(0.1f, 0.04f),
        };

        originalBorderColor = textBox.BorderColor;

        textBox.TextChanged += box =>
        {
            if (
                HasAlpha
                    ? box.Text.TryParseColorFromHexRgba(out var color)
                    : box.Text.TryParseColorFromHexRgb(out color)
            )
            {
                box.BorderColor = originalBorderColor;
                box.BorderEnabled = false;

                sample.BorderColor = color;

                if (color != PropertyGetter())
                    PropertySetter(color);

                var text = HasAlpha ? color.ToHexStringRgba() : color.ToHexStringRgb();
                if (text != box.Text)
                    box.Text = text;
            }
            else
            {
                box.BorderColor = Color.Red;
                box.BorderEnabled = true;
            }
        };

        var label = Tools.Tools.GetLabelOrDefault(name, Label);

        sample.ButtonClicked += _ => OpenColorPicker(label, sample, textBox);

        sample.SetToolTip(Description);
        textBox.SetToolTip(Description);

        return new List<Control>()
        {
            new Control(new MyGuiControlLabel(text: label), minWidth: Control.LabelMinWidth),
            new Control(sample, offset: new Vector2(0f, 0.005f)),
            new Control(textBox, fixedWidth: textBox.Size.X),
        };

        void OpenColorPicker(string caption, MyGuiControlButton swatch, MyGuiControlTextbox box)
        {
            var current = PropertyGetter();

            var dialog = new MyGuiScreenDialogColor(MyStringId.GetOrCompute(caption));
            SeedDialogColor(dialog, current);

            dialog.OnConfirmed += (r, g, b) =>
            {
                // The dialog only picks RGB, so the alpha channel is carried over unchanged.
                var color = new Color(r, g, b) { A = current.A };

                PropertySetter(color);

                swatch.BorderColor = color;
                box.BorderColor = originalBorderColor;
                box.BorderEnabled = false;
                box.Text = HasAlpha ? color.ToHexStringRgba() : color.ToHexStringRgb();
            };

            MyGuiSandbox.AddScreen(dialog);
        }

        void PropertySetter(Color color) => propertySetter(color);
        Color PropertyGetter() => (Color)propertyGetter();
    }

    private static void SeedDialogColor(MyGuiScreenDialogColor dialog, Color color)
    {
        // Setting the color also raises OnChange, which updates the value the dialog
        // confirms with, so an unmodified picker returns the color it was opened on.
        if (DialogColorControlField?.GetValue(dialog) is MyGuiControlColor control)
            control.SetColor(new Color(color.R, color.G, color.B));
    }

    public List<Type> SupportedTypes { get; } = new List<Type>() { typeof(Color) };
}
