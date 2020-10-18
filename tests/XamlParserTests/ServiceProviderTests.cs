using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using XamlX.Runtime;
using XamlX.TypeSystem;
using Xunit;

namespace XamlParserTests
{
    public class ServiceProviderTests : CompilerTestBase
    {
        private void CompileAndRun(string xaml, CallbackExtensionCallback cb, IXamlParentStackProviderV1 parentStack)
            => Compile(xaml).create(new DictionaryServiceProvider
            {
                [typeof(CallbackExtensionCallback)] = cb,
                [typeof(IXamlParentStackProviderV1)] = parentStack
            });
        
        [Theory,
        InlineData(true),
        InlineData(false)]
        public void Parent_Stack_Should_Provide_Info_About_Parents(bool importParents)
        {
            var importedParents = importParents
                ? new ListParentsProvider
                {
                    "Parent1",
                    "Parent2"
                }
                : null;
            int num = 0;
            CompileAndRun(@"
<ServiceProviderTestsClass xmlns='test' Id='root' Property='{Callback}'>
    <ServiceProviderTestsClass.Child>
        <ServiceProviderTestsClass Id='direct' Property='{Callback}'/>
    </ServiceProviderTestsClass.Child>
    <ServiceProviderTestsClass Id='content' Property='{Callback}'/> 
</ServiceProviderTestsClass>", sp =>
            {
                //Manual unrolling of enumerable, useful for state tracking
                var stack = new List<object>();
                var parentsEnumerable = sp.GetService<IXamlParentStackProviderV1>().Parents;
                using (var parentsEnumerator = parentsEnumerable.GetEnumerator())
                {
                    while (parentsEnumerator.MoveNext())
                        stack.Add(parentsEnumerator.Current);
                }

                var baseCount = 0;
                if (importParents)
                {
                    baseCount = 2;
                    Assert.Equal("Parent1", stack[stack.Count - 2]);
                    Assert.Equal("Parent2", stack.Last());
                }

                if (num == 0)
                {
                    Assert.Equal(baseCount + 1, stack.Count);
                    Assert.Equal("root", ((ServiceProviderTestsClass) stack[0]).Id);
                }
                else if (num == 1)
                {
                    Assert.Equal(baseCount + 2, stack.Count);
                    Assert.Equal("direct", ((ServiceProviderTestsClass) stack[0]).Id);
                    Assert.Equal("root", ((ServiceProviderTestsClass) stack[1]).Id);
                }
                else if (num == 2)
                {
                    Assert.Equal(baseCount + 2, stack.Count);
                    Assert.Equal("content", ((ServiceProviderTestsClass) stack[0]).Id);
                    Assert.Equal("root", ((ServiceProviderTestsClass) stack[1]).Id);
                }
                else
                {
                    throw new InvalidOperationException();
                }

                num++;
                
                return "Value";
            }, importedParents);
        }
        
        [Fact]
        public void TypeDescriptor_Stubs_Are_Somewhat_Usable()
        {
            CompileAndRun(@"<ServiceProviderTestsClass xmlns='test' Property='{Callback}'/>", sp =>
            {
                var tdc = (ITypeDescriptorContext) sp;
                Assert.Equal(tdc, sp.GetService<ITypeDescriptorContext>());
                Assert.Null(tdc.Instance);
                Assert.Null(tdc.Container);
                Assert.Null(tdc.PropertyDescriptor);
                Assert.Throws<NotSupportedException>(() => tdc.OnComponentChanging());
                Assert.Throws<NotSupportedException>(() => tdc.OnComponentChanged());
                
                return "Value";
            }, null);
        }
        
        [Fact]
        public void ProvideValueTarget_Provides_Info_About_Properties()
        {
            var num = 0;
            CompileAndRun(@"
<ServiceProviderTestsClass xmlns='test' 
    Property='{Callback}'
    ServiceProviderTestsClassForAttached.AttachedProperty='{Callback}'
/>", sp =>
            {
                var pt = sp.GetService<ITestProvideValueTarget>();
                Assert.IsType<ServiceProviderTestsClass>(pt.TargetObject);
                if(num == 0)
                {
                    Assert.Equal("Property", pt.TargetProperty);
                }
                else if (num == 1)
                {
                    Assert.Equal("1", ((ServiceProviderTestsClass) pt.TargetObject).Property);
                    Assert.Equal("AttachedProperty", pt.TargetProperty);
                }
                else
                {
                    throw new InvalidOperationException();
                }

                num++;
                return num.ToString();
            }, null);
            Assert.Equal(2, num);
        }
        
        [Fact]
        public void Inner_Provider_Interception_Works()
        {

            Configuration.TypeMappings.InnerServiceProviderFactoryMethod =
                Configuration.TypeSystem.GetType(typeof(InnerProvider).FullName)
                    .FindMethod(m => m.Name == "InnerProviderFactory");
                    
            CompileAndRun(@"<ServiceProviderTestsClass xmlns='test' Property='{Callback}'/>", sp =>
            {
                var rootProv = (InnerProvider)sp.GetService<ITestRootObjectProvider>();
                Assert.IsType<ServiceProviderTestsClass>(rootProv.OriginalRootObject);
                Assert.IsType<string>(rootProv.RootObject);
                
                return "Value";
            }, null);
        }

        [Fact]
        public void Namespace_Info_Should_Be_Preserved()
        {
            CompileAndRun(@"
<ServiceProviderTestsClass 
    xmlns='test'
    xmlns:clr1='clr-namespace:System.Collections.Generic;assembly=netstandard'
    xmlns:clr2='clr-namespace:Dummy;assembly=TestClasses'
    Property='{Callback}'/>", sp =>
            {
                var nsList = sp.GetService<IXamlXmlNamespaceInfoProviderV1>().XmlNamespaces;
                // Direct calls without struct diff because of EntryPointNotFoundException issue before
                Assert.True(nsList.TryGetValue("clr1", out var xlst));
                Assert.Equal("System.Collections.Generic", xlst[0].ClrNamespace);
                Helpers.StructDiff(nsList,
                    new Dictionary<string, IReadOnlyList<XamlXmlNamespaceInfoV1>>
                    {
                        [""] = new List<XamlXmlNamespaceInfoV1>
                        {
                            new XamlXmlNamespaceInfoV1
                            {
                                ClrNamespace = "XamlParserTests",
                                ClrAssemblyName = typeof(ServiceProviderTestsClass).Assembly.GetName().Name
                            }
                        },
                        ["clr1"] = new List<XamlXmlNamespaceInfoV1>
                        {
                            new XamlXmlNamespaceInfoV1
                            {
                                ClrNamespace = "System.Collections.Generic",
                                ClrAssemblyName = "netstandard"
                            }
                        },
                        ["clr2"] = new List<XamlXmlNamespaceInfoV1>
                        {
                            new XamlXmlNamespaceInfoV1
                            {
                                ClrNamespace = "Dummy",
                                ClrAssemblyName = "TestClasses"
                            }
                        }
                    });
                
                return "Value";
            }, null);
        }
        
        [Fact]
        public void Uri_Context_Is_Usable()
        {
            bool ok = false;
            CompileAndRun(@"<ServiceProviderTestsClass xmlns='test' Property='{Callback}'/>", sp =>
            {
                Assert.Equal("http://example.com/", sp.GetService<ITestUriContext>().BaseUri.ToString());
                ok = true;
                return "Value";
            }, null);
            Assert.True(ok);
        }
        
        [Fact]
        public void Unknown_Services_Should_Return_null()
        {
            UnknownServiceUsageExtension.ProvideValueEventRequiredAssert += UnknownServiceUsageExtension_ProvideValueEventRequiredAssert;

            var res = (ServiceProviderTestsClass)CompileAndRun(
                @"<ServiceProviderTestsClass xmlns='test' Property='{UnknownServiceUsage Return=123}'/>");
            Assert.Equal("123", res.Property);

            UnknownServiceUsageExtension.ProvideValueEventRequiredAssert -= UnknownServiceUsageExtension_ProvideValueEventRequiredAssert;
        }

        private void UnknownServiceUsageExtension_ProvideValueEventRequiredAssert(IServiceProvider provider)
        {
            Assert.Null(provider.GetService(typeof(string)));
        }
    }
}