namespace OmniXaml.Tests
{
    using System.Collections.Generic;
    using System.Reflection;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Model;

    [TestClass]
    public class ObjectBuilderTests
    {
        [TestMethod]
        public void TemplateContent()
        {
            var node = new ConstructionNode(typeof(ItemsControl))
            {
                Assignments = new List<PropertyAssignment>
                {
                    new PropertyAssignment
                    {
                        Property = Property.RegularProperty<ItemsControl>(control => control.ItemTemplate),
                        Children = new List<ConstructionNode>
                        {
                            new ConstructionNode(typeof(DataTemplate))
                            {
                                Assignments = new[]
                                {
                                    new PropertyAssignment
                                    {
                                        Property = Property.RegularProperty<DataTemplate>(template => template.Content),
                                        Children = new[] { new ConstructionNode(typeof(TextBlock)),}
                                    }
                                }
                            }
                        }
                    },
                }
            };

            var obj = Create(node);
        }

        [TestMethod]
        public void GivenSimpleExtensionThatProvidesAString_TheStringIsProvided()
        {
            var constructionNode = new ConstructionNode(typeof(SimpleExtension))
            {
                Assignments =
                    new List<PropertyAssignment>
                    {
                        new PropertyAssignment
                        {
                            Property = Property.RegularProperty<SimpleExtension>(extension => extension.Property),
                            SourceValue = "MyText"
                        }
                    }
            };

            var node = new ConstructionNode(typeof(TextBlock))
            {
                Assignments = new[]{new PropertyAssignment()
            {
                Property = Property.RegularProperty<TextBlock>(tb => tb.Text),
                Children = new []{ constructionNode}
            }, }
            };

            var b = Create(node);

            Assert.AreEqual(new TextBlock() { Text = "MyText" }, b);
        }

        [TestMethod]
        public void ImmutableFromContent()
        {
            var node = new ConstructionNode(typeof(MyImmutable)) { InjectableArguments = new[] { "Hola" } };
            var myImmutable = new MyImmutable("Hola");
            var actual = Create(node);

            Assert.AreEqual(myImmutable, actual);
        }

        private static object Create(ConstructionNode node)
        {
            var builder = new ExtendedObjectBuilder(new InstanceCreator(), new SourceValueConverter());
            return builder.Create(node);
        }
    }
}