using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.JSInterop;

namespace DV.Admin.UI.Components.Layout
{
    public class LayoutSelector : ComponentBase
    {
        [Inject] protected IJSRuntime JS { get; set; } = default!;

        [Parameter]
        public RenderFragment? ChildContent { get; set; }

        [Parameter(CaptureUnmatchedValues = true)]
        public Dictionary<string, object>? AdditionalAttributes { get; set; }

        private bool _isInIFrame;
        private RenderFragment? _layoutContent;

        protected override async Task OnInitializedAsync()
        {
            try
            {
                _isInIFrame = await JS.InvokeAsync<bool>("isInIFrame");
            }
            catch
            {
                _isInIFrame = false;
            }
        }

        protected override void OnParametersSet()
        {
            _layoutContent = builder =>
            {
                // Select layout based on iframe status
                var layoutType = _isInIFrame
                    ? typeof(MinimalLayout)
                    : typeof(MainLayout);

                builder.OpenComponent(0, layoutType);
                builder.AddAttribute(1, "Body", ChildContent);
                builder.CloseComponent();
            };
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (_layoutContent != null)
            {
                builder.AddContent(0, _layoutContent);
            }
        }
    }
}
