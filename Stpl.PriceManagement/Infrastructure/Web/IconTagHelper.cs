using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Stpl.PriceManagement.Infrastructure.Web
{
    /// <summary>
    /// Renders one icon from the sprite in Views/Shared/_IconSprite.cshtml.
    /// </summary>
    /// <remarks>
    /// A tag helper rather than a partial per icon: the fare grid draws one of
    /// these on every departure row, and a partial would resolve and execute a
    /// view several hundred times in a single response. This writes a fixed
    /// string.
    ///
    /// The icon takes its colour from whatever it sits inside, because the
    /// sprite's symbols stroke with currentColor. That is why there is no
    /// colour attribute here - put the colour on the tile, chip or button.
    ///
    /// An icon is decorative by default: the text beside it is the label, and
    /// a screen reader announcing both would say everything twice. Set label
    /// only when the icon IS the label, which usually means the control around
    /// it has no text - and even then, prefer aria-label on that control.
    /// </remarks>
    [HtmlTargetElement("st-icon", TagStructure = TagStructure.WithoutEndTag)]
    public sealed class IconTagHelper : TagHelper
    {
        /// <summary>The symbol name without the "i-" prefix, e.g. "search".</summary>
        public string Name { get; set; }

        /// <summary>Extra classes, e.g. "icon--16".</summary>
        public string Class { get; set; }

        /// <summary>Accessible name. Null - the default - means decorative.</summary>
        public string Label { get; set; }

        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            output.TagName = "svg";
            output.TagMode = TagMode.StartTagAndEndTag;

            output.Attributes.SetAttribute(
                "class", string.IsNullOrEmpty(Class) ? "icon" : "icon " + Class);
            output.Attributes.SetAttribute("focusable", "false");

            if (string.IsNullOrEmpty(Label))
            {
                output.Attributes.SetAttribute("aria-hidden", "true");
            }
            else
            {
                output.Attributes.SetAttribute("role", "img");
                output.Attributes.SetAttribute("aria-label", Label);
            }

            output.Content.SetHtmlContent("<use href=\"#i-" + Name + "\" />");
        }
    }
}
