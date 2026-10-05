// V2.9.3：Windows 7 兼容构建（net6.0-windows）所需的编译期 polyfill。
//
// C# 11 的 `required` 成员依赖三个属性类型，它们从 .NET 7 起才在内置库里：
//   System.Runtime.CompilerServices.RequiredMemberAttribute
//   System.Runtime.CompilerServices.CompilerFeatureRequiredAttribute
//   System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute
// .NET 6 没有它们，编译器会直接报 CS0656（「缺少编译器要求的成员」）。
// 这里按官方文档给出的 polyfill 方式补上（internal，不污染外部可见 API），
// net7.0 及以上目标下整份文件都是空的。
//
// 为什么值得这么绕：兼容构建的目标是**同一份源码**，而不是把 `required` 全部改写成构造函数 ——
// 后者要在几十处来回改，改坏的风险远高于补三个空属性。
#if !NET7_0_OR_GREATER

namespace System.Runtime.CompilerServices
{
    /// <summary>标记「必须由对象初始化器赋值」的成员。</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property,
        AllowMultiple = false, Inherited = false)]
    internal sealed class RequiredMemberAttribute : Attribute
    {
    }

    /// <summary>标记「该成员用到了某个语言特性」。</summary>
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName) => FeatureName = featureName;

        public string FeatureName { get; }

        public bool IsOptional { get; init; }

        public const string RefStructs = nameof(RefStructs);
        public const string RequiredMembers = nameof(RequiredMembers);
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>标记「构造函数已经把 required 成员都赋值了」。</summary>
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    internal sealed class SetsRequiredMembersAttribute : Attribute
    {
    }
}

#endif
