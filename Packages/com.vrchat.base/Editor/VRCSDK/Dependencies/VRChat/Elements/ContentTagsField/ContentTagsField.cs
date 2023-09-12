using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

[assembly: UxmlNamespacePrefix("VRC.SDKBase.Editor.Elements", "vrc")]
namespace VRC.SDKBase.Editor.Elements
{
    public class ContentTagsField : VisualElement
    {
        private static readonly string[] CONTENT_TAGS = { "content_sex", "content_violence", "content_gore", "content_other" };

        private static string GetContentTagName(string tag)
        {
            switch (tag)
            {
                case "content_sex":
                    return "Nudity/Sexuality";
                case "content_violence":
                    return "Realistic Violence";
                case "content_gore":
                    return "Blood/Gore";
                case "content_other":
                    return "Other NSFW";
                default:
                    return tag;
            }
        }

        private VisualElement _tagsContainer;
        private Label _tagsLabel;

        private IList<string> _tags;
        public IList<string> tags
        {
            get => _tags;
            set
            {
                _tags = value;
                UpdateTags(ref _tagsContainer);
            }
        }

        public EventHandler<string> OnToggleTag;

        public new class UxmlFactory : UxmlFactory<ContentTagsField, UxmlTraits> {}

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            private UxmlStringAttributeDescription _label = new UxmlStringAttributeDescription { name = "label" };
            
            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription
            {
                get { yield break; }
            }
            
            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var tagsField = (ContentTagsField) ve;
                var label = _label.GetValueFromBag(bag, cc);
                if (string.IsNullOrWhiteSpace(label))
                {
                    tagsField.Q<Label>("tags-label").AddToClassList("d-none");
                }
                else
                {
                    tagsField.Q<Label>("tags-label").text = label;
                }
            }
        }
        
        public ContentTagsField()
        {
            Resources.Load<VisualTreeAsset>("ContentTagsField").CloneTree(this);
            styleSheets.Add(Resources.Load<StyleSheet>("ContentTagsFieldStyles"));
            
            _tagsContainer = this.Q(null, "tags-row");
            _tagsLabel = this.Q<Label>("tags-label");
            tags = new List<string>();
        }
        
        private VisualElement CreateTag(string tag)
        {
            var tagElement = new Button(() => OnToggleTag?.Invoke(this, tag));
            tagElement.AddToClassList("content-tag");
            tagElement.SetEnabled(!IsLocked(tag));

            var tagToggle = new Toggle();
            tagToggle.value = tags.Contains(tag);
            tagToggle.RegisterValueChangedCallback((val) => OnToggleTag?.Invoke(this, tag));
            tagElement.Add(tagToggle);

            var tagText = GetContentTagName(tag);
            var tagLabel = new Label(tagText);
            tagLabel.AddToClassList("tag-label");
            tagElement.Add(tagLabel);

            return tagElement;
        }

        private void UpdateTags(ref VisualElement tagContainer)
        {
            if (tagContainer == null)
            {
                return;
            }
            tagContainer.Clear();
            foreach (var tag in CONTENT_TAGS)
            {
                tagContainer.Add(CreateTag(tag));
            }
        }

        private bool IsLocked(string tag)
        {
            return tags.Contains("admin_content_reviewed") || tags.Contains("admin_" + tag);
        }
    }
}