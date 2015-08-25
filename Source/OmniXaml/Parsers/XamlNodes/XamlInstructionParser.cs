﻿namespace OmniXaml.Parsers.XamlNodes
{
    using System.Collections.Generic;
    using MarkupExtensions;
    using ProtoParser;
    using Sprache;
    using Typing;
    using XamlParseException = OmniXaml.XamlParseException;

    public class XamlInstructionParser : IXamlInstructionParser
    {
        private readonly IWiringContext wiringContext;
        private IEnumerator<ProtoXamlInstruction> nodeStream;

        public XamlInstructionParser(IWiringContext wiringContext)
        {
            this.wiringContext = wiringContext;
        }

        private bool EndOfStream { get; set; }

        private bool CurrentNodeIsElement => CurrentNodeType == NodeType.Element || CurrentNodeType == NodeType.EmptyElement;

        private NodeType CurrentNodeType => nodeStream.Current?.NodeType ?? NodeType.None;

        private bool IsNestedPropertyImplicit => CurrentNodeType != NodeType.PropertyElement && CurrentNodeType != NodeType.EmptyPropertyElement &&
                                                 CurrentNodeType != NodeType.EndTag;

        private bool CurrentNodeIsText => CurrentNodeType == NodeType.Text;

        private ProtoXamlInstruction Current => nodeStream.Current;
        private string CurrentText => nodeStream.Current.Text;
        private string CurrentPropertyText => Current.PropertyAttributeText;
        private XamlMemberBase CurrentMember => Current.PropertyAttribute;

        public IEnumerable<XamlInstruction> Parse(IEnumerable<ProtoXamlInstruction> protoNodes)
        {
            nodeStream = protoNodes.GetEnumerator();
            SetNextNode();

            foreach (var prefix in ParsePrefixDefinitions())
            {
                yield return prefix;
            }
            foreach (var element in ParseElements())
            {
                yield return element;
            }
        }

        private IEnumerable<XamlInstruction> ParseElements(XamlMember hostingProperty = null)
        {
            if (hostingProperty != null)
            {
                yield return Inject.StartOfMember(hostingProperty);
            }

            if (CurrentNodeIsText)
            {
                yield return Inject.Value(CurrentText);
            }

            while (CurrentNodeIsElement && !EndOfStream)
            {
                switch (nodeStream.Current.NodeType)
                {
                    case NodeType.Element:
                        foreach (var xamlNode in ParseNonEmptyElement())
                        {
                            yield return xamlNode;
                        }

                        break;
                    case NodeType.EmptyElement:
                        foreach (var xamlNode in ParseEmptyElement())
                        {
                            yield return xamlNode;
                        }
                        break;
                }

                // There may be text nodes after each element. Skip all of them.
                SkipTextNodes();
            }

            if (hostingProperty != null)
            {
                yield return Inject.EndOfMember();
            }
        }

        private void SkipTextNodes()
        {
            while (CurrentNodeType == NodeType.Text)
            {
                SetNextNode();
            }
        }

        private IEnumerable<XamlInstruction> ParseEmptyElement()
        {
            yield return Inject.StartOfObject(nodeStream.Current.XamlType);

            SetNextNode();

            foreach (var member in ParseMembersOfObject())
            {
                yield return member;
            }

            yield return Inject.EndOfObject();

            if (CurrentNodeType == NodeType.Text)
            {
                SetNextNode();
            }
        }

        private IEnumerable<XamlInstruction> ParseNonEmptyElement()
        {
            yield return Inject.StartOfObject(nodeStream.Current.XamlType);
            var parentType = nodeStream.Current.XamlType;

            if (parentType.NeedsConstructionParameters)
            {
                foreach (var node in InjectNodesForTypeThatRequiresInitialization())
                {
                    yield return node;
                }
            }
            else
            {
                SetNextNode();

                foreach (var node in ParseMembersOfObject())
                {
                    yield return node;
                }
                foreach (var node in ParseContentPropertyIfAny(parentType))
                {
                    yield return node;
                }

                SkipTextNodes();

                foreach (var xamlNode in ParseNestedProperties(parentType))
                {
                    yield return xamlNode;
                }
            }

            yield return Inject.EndOfObject();
            ReadEndTag();
        }

        private IEnumerable<XamlInstruction> InjectNodesForTypeThatRequiresInitialization()
        {
            yield return Inject.Initialization();
            SetNextNode();
            yield return Inject.Value(CurrentText);
            yield return Inject.EndOfMember();
        }

        private void ReadEndTag()
        {
            SkipTextNodes();

            if (CurrentNodeType != NodeType.EndTag)
            {
                throw new XamlParseException("Expected End Tag");
            }

            SetNextNode();
        }

        private IEnumerable<XamlInstruction> ParseNestedProperties(XamlType parentType)
        {
            while (CurrentNodeType == NodeType.PropertyElement || CurrentNodeType == NodeType.EmptyPropertyElement)
            {
                var member = nodeStream.Current.PropertyElement;
                if (member.XamlType.IsCollection)
                {
                    SetNextNode();
                    foreach (var xamlNode in ParseCollectionInsideThisProperty(member))
                    {
                        yield return xamlNode;
                    }
                }
                else
                {
                    foreach (var xamlNode in ParseNestedProperty(member))
                    {
                        yield return xamlNode;
                    }
                }

                // After and EndTag, there could be a ContentProperty! so we consider parsing it.
                if (CurrentNodeType == NodeType.EndTag)
                {
                    SetNextNode();
                    foreach (var xamlNode in ParseContentPropertyIfAny(parentType))
                    {
                        yield return xamlNode;
                    }
                }
            }
        }

        private IEnumerable<XamlInstruction> ParseContentPropertyIfAny(XamlType parentType)
        {
            if (IsNestedPropertyImplicit)
            {
                var contentProperty = parentType.ContentProperty;
                if (contentProperty == null)
                {
                    throw new XamlParseException($"Cannot get the content property for the type {parentType}");
                }

                if (contentProperty.XamlType.IsCollection)
                {
                    foreach (var xamlNode in ParseCollectionInsideThisProperty(contentProperty))
                    {
                        yield return xamlNode;
                    }
                }
                else
                {
                    foreach (var xamlNode in ParseElements(contentProperty))
                    {
                        yield return xamlNode;
                    }
                }
            }
        }

        private void SetNextNode()
        {
            if (EndOfStream)
            {
                throw new XamlParseException("The end of the stream has already been reached!");
            }

            EndOfStream = !nodeStream.MoveNext();
        }

        private IEnumerable<XamlInstruction> ParseCollectionInsideThisProperty(XamlMember member)
        {
            yield return Inject.StartOfMember(member);
            yield return Inject.GetObject();
            yield return Inject.Items();

            foreach (var xamlNode in ParseElements())
            {
                yield return xamlNode;
            }

            yield return Inject.EndOfMember();
            yield return Inject.EndOfObject();
            yield return Inject.EndOfMember();
        }

        private IEnumerable<XamlInstruction> ParseNestedProperty(XamlMember member)
        {
            yield return Inject.StartOfMember(member);

            SetNextNode();

            foreach (var xamlNode in ParseInnerContentOfNestedProperty())
            {
                yield return xamlNode;
            }

            yield return Inject.EndOfMember();
        }

        private IEnumerable<XamlInstruction> ParseInnerContentOfNestedProperty()
        {
            if (CurrentNodeType == NodeType.Text)
            {
                yield return Inject.Value(nodeStream.Current.Text);
            }
            else
            {
                foreach (var xamlNode in ParseElements())
                {
                    yield return xamlNode;
                }
            }
        }

        private IEnumerable<XamlInstruction> ParseMembersOfObject()
        {
            while (CurrentNodeType == NodeType.Attribute && !EndOfStream)
            {
                var valueOfMember = CurrentPropertyText;

                yield return Inject.StartOfMember(CurrentMember);

                if (IsMarkupExtension(valueOfMember))
                {
                    foreach (var xamlNode in ParseMarkupExtension(valueOfMember))
                    {
                        yield return xamlNode;
                    }
                }
                else
                {
                    yield return Inject.Value(valueOfMember);
                }

                yield return Inject.EndOfMember();

                SetNextNode();
            }
        }

        private IEnumerable<XamlInstruction> ParseMarkupExtension(string valueOfMember)
        {
            var tree = MarkupExtensionParser.MarkupExtension.Parse(valueOfMember);
            var markupExtensionConverter = new MarkupExtensionNodeToXamlNodesConverter(wiringContext);
            return markupExtensionConverter.Convert(tree);
        }

        private IEnumerable<XamlInstruction> ParsePrefixDefinitions()
        {
            while (CurrentNodeType == NodeType.PrefixDefinition)
            {
                var protoXamlNode = nodeStream.Current;
                yield return Inject.PrefixDefinitionOfNamespace(protoXamlNode);
                SetNextNode();
            }
        }

        private static bool IsMarkupExtension(string text)
        {
            return text.Length > 3 && text.StartsWith("{") && text.EndsWith("}");
        }
    }
}