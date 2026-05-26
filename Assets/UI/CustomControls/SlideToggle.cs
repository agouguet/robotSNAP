using UnityEngine;
using UnityEngine.UIElements;

namespace RobotSNAP.UI
{
    /// <summary>
    /// Custom control for a slide toggle, similar to a switch in mobile applications.
    /// </summary>
    [UxmlElement]
    public partial class SlideToggle : BaseField<bool>
    {
        // USS class names
        public static readonly new string ussClassName = "slide-toggle";
        public static readonly new string inputUssClassName = "slide-toggle__input";
        public static readonly string inputKnobUssClassName = "slide-toggle__input-knob";
        public static readonly string inputCheckedUssClassName = "slide-toggle__input--checked";

        private VisualElement m_Input;
        private VisualElement m_Knob;
        private VisualElement m_Icon;
        private Texture2D m_IconTexture;

        /// <summary>
        /// Gets or sets the icon texture displayed to the left of the label.
        /// </summary>
        [UxmlAttribute]
        public Texture2D iconTexture
        {
            get => m_IconTexture;
            set
            {
                m_IconTexture = value;
                if (m_IconTexture != null)
                {
                    if (m_Icon == null)
                    {
                        m_Icon = new VisualElement();
                        m_Icon.AddToClassList("slide-toggle__icon");
                        Insert(0, m_Icon);
                    }
                    m_Icon.style.backgroundImage = new StyleBackground(m_IconTexture);
                }
                else if (m_Icon != null)
                {
                    m_Icon.RemoveFromHierarchy();
                    m_Icon = null;
                }
            }
        }

        /// <summary>
        /// Default constructor (required for UXML).
        /// </summary>
        public SlideToggle() : this(null) { }

        /// <summary>
        /// Constructor that allows setting a label and an optional icon (via Resources path).
        /// </summary>
        /// <param name="label">The text label displayed next to the toggle.</param>
        /// <param name="iconImagePath">Path to a texture inside a Resources folder (e.g. "Icons/moon").</param>
        public SlideToggle(string label, string iconImagePath = null) : base(label, null)
        {
            AddToClassList(ussClassName);
            focusable = false;

            // Get the BaseField's input element (the track background)
            m_Input = this.Q(className: BaseField<bool>.inputUssClassName);
            m_Input.AddToClassList(inputUssClassName);

            // Create the knob
            m_Knob = new VisualElement();
            m_Knob.AddToClassList(inputKnobUssClassName);
            m_Input.Add(m_Knob);

            // Register event handlers
            RegisterCallback<ClickEvent>(OnClick);
            RegisterCallback<NavigationSubmitEvent>(OnSubmit);
            RegisterCallback<KeyDownEvent>(OnKeydownEvent);

            // Set initial icon if provided
            if (!string.IsNullOrEmpty(iconImagePath))
            {
                var texture = Resources.Load<Texture2D>(iconImagePath);
                if (texture != null)
                    iconTexture = texture;
                else
                    Debug.LogWarning($"SlideToggle: Icon not found at Resources/{iconImagePath}");
            }
        }

        // --- Event handlers ---
        private static void OnClick(ClickEvent evt)
        {
            if (evt.currentTarget is SlideToggle st)
                st.ToggleValue();
        }

        private static void OnSubmit(NavigationSubmitEvent evt)
        {
            if (evt.currentTarget is SlideToggle st)
                st.ToggleValue();
        }

        private static void OnKeydownEvent(KeyDownEvent evt)
        {
            if (evt.currentTarget is SlideToggle st && st.panel?.contextType != ContextType.Player)
            {
                if (evt.keyCode == KeyCode.KeypadEnter || evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.Space)
                    st.ToggleValue();
            }
        }

        // --- Core logic ---
        private void ToggleValue() => value = !value;

        public override void SetValueWithoutNotify(bool newValue)
        {
            base.SetValueWithoutNotify(newValue);
            m_Input.EnableInClassList(inputCheckedUssClassName, newValue);
        }
    }
}