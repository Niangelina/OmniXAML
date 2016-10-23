﻿namespace WpfApplication1.Context
{
    using System.Windows;
    using OmniXaml;
    using OmniXaml.TypeLocation;

    public class XamlLoader
    {
        private readonly MetadataProvider metadataProvider;
        private readonly TypeDirectory directory;

        public XamlLoader()
        {
            metadataProvider = new MetadataProvider();
            

            var type = typeof(Window);
            var ass = type.Assembly;

            directory = new TypeDirectory();
            directory.RegisterPrefix(new PrefixRegistration(string.Empty, "root"));

            var configuredAssemblyWithNamespaces = Route
                .Assembly(ass)
                .WithNamespaces("System.Windows", "System.Windows.Controls");
            var xamlNamespace = XamlNamespace
                .Map("root")
                .With(configuredAssemblyWithNamespaces);
            directory.AddNamespace(xamlNamespace);
        }

        public object Load(string xaml)
        {
            var constructionContext = new ConstructionContext(
                new InstanceCreator(),
                Registrator.GetSourceValueConverter(),
                metadataProvider,
                new InstanceLifecycleSignaler());

            var objectBuilder = new ExtendedObjectBuilder(constructionContext, (assignment, context) => new MarkupExtensionContext(assignment, context, directory));
            var cons = GetConstructionNode(xaml);
            return objectBuilder.Create(cons);
        }

        private ConstructionNode GetConstructionNode(string xaml)
        {
            var sut = new XamlToTreeParser(directory, new MetadataProvider(), new[] { new InlineParser(directory), });
            var tree = sut.Parse(xaml);
            return tree;
        }
    }
}