using System.Collections.Generic;
using System.Reflection;
using XamlX.TypeSystem;
using Xunit;

namespace XamlX.Tests
{
    public class XamlAssemblyListTests
    {
        private readonly int _initCapacity;

        public XamlAssemblyListTests()
        {
            _initCapacity = (int)typeof(XamlAssemblyList<IXamlAssembly>)
                .GetField("InitCapacity", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);
        }

        [Fact]
        public void Add_Item_Count_Increased_For()
        {
            var item = new XamlAssemblyMock();
            var list = new XamlAssemblyList<IXamlAssembly>
            {
                item
            };

            int idx;
            for (idx = 0; idx < list.Count; ++idx)
            {
                if (idx == _initCapacity + 1)
                    break;
                list.Add(item);
            }

            Assert.Equal(_initCapacity + 1, idx);
        }

        [Fact]
        public void Add_Item_Count_Increased_Foreach()
        {
            var item = new XamlAssemblyMock();
            var list = new XamlAssemblyList<IXamlAssembly>
            {
                item
            };

            int idx = 0;
            foreach (var _ in list)
            {
                if (idx >= list.Count || idx == _initCapacity + 1)
                    break;
                list.Add(item);
                ++idx;
            }

            Assert.Equal(_initCapacity + 1, idx);
        }

        private class XamlAssemblyMock : IXamlAssembly
        {
            public string Name =>
                throw new System.NotImplementedException();

            public IReadOnlyList<IXamlCustomAttribute> CustomAttributes =>
                throw new System.NotImplementedException();

            public bool Equals(IXamlAssembly other) =>
                throw new System.NotImplementedException();

            public IXamlType FindType(string fullName) =>
                throw new System.NotImplementedException();
        }
    }
}
