using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;

using NHaml.Configuration;
using NHaml.Exceptions;
using NHaml.Rules;
using NHaml.Utilities;

namespace NHaml
{
  public sealed class TemplateCompiler
  {
    private static readonly Regex _pathCleaner
      = new Regex(@"[-\\/\.:\s]", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly string[] DefaultAutoClosingTags
      = new[] {"META", "IMG", "LINK", "BR", "HR", "INPUT"};

    private readonly StringSet _autoClosingTags
      = new StringSet(DefaultAutoClosingTags);

    private static readonly string[] DefaultUsings
      = new[] {"System", "System.IO", "NHaml", "NHaml.Utilities"};

    private readonly StringSet _usings
      = new StringSet(DefaultUsings);

    private static readonly string[] DefaultReferences
      = new[]
          {
            typeof(INotifyPropertyChanged).Assembly.Location,
            typeof(TemplateCompiler).Assembly.Location
          };

    private readonly StringSet _references
      = new StringSet(DefaultReferences);

    private readonly MarkupRule[] _markupRules = new MarkupRule[128];

    private Type _viewBaseType;
    private bool _isProduction;

    public TemplateCompiler()
    {
      AddRule(new EofMarkupRule());
      AddRule(new DocTypeMarkupRule());
      AddRule(new TagMarkupRule());
      AddRule(new ClassMarkupRule());
      AddRule(new IdMarkupRule());
      AddRule(new EvalMarkupRule());
      AddRule(new SilentEvalMarkupRule());
      AddRule(new PreambleMarkupRule());
      AddRule(new CommentMarkupRule());
      AddRule(new EscapeMarkupRule());
      AddRule(new PartialMarkupRule());

      ViewBaseType = typeof(object);

      LoadFromConfiguration();
    }

    public void LoadFromConfiguration()
    {
      var section = NHamlSection.Read();

      if (section != null)
      {
        _isProduction = section.Production;

        foreach (var assemblyConfigurationElement in section.Assemblies)
        {
          AddReference(Assembly.Load(assemblyConfigurationElement.Name).Location);
        }

        foreach (var namespaceConfigurationElement in section.Namespaces)
        {
          AddUsing(namespaceConfigurationElement.Name);
        }
      }
    }

    public bool IsProduction
    {
      get { return _isProduction; }
    }

    public Type ViewBaseType
    {
      get { return _viewBaseType; }
      set
      {
        value.ArgumentNotNull("value");

        _viewBaseType = value;

        _usings.Add(_viewBaseType.Namespace);
        _references.Add(_viewBaseType.Assembly.Location);
      }
    }

    public void AddUsing(string @namespace)
    {
      @namespace.ArgumentNotEmpty("namespace");

      _usings.Add(@namespace);
    }

    public IEnumerable Usings
    {
      get { return _usings; }
    }

    public void AddReference(string assemblyLocation)
    {
      assemblyLocation.ArgumentNotEmpty("assemblyLocation");

      _references.Add(assemblyLocation);
    }

    public void AddReferences(Type type)
    {
      AddReference(type.Assembly.Location);

      if (type.IsGenericType)
      {
        foreach (var t in type.GetGenericArguments())
        {
          AddReferences(t);
        }
      }
    }

    public IEnumerable References
    {
      get { return _references; }
    }

    public void AddRule(MarkupRule markupRule)
    {
      markupRule.ArgumentNotNull("markupRule");

      _markupRules[markupRule.Signifier] = markupRule;
    }

    public MarkupRule GetRule(InputLine inputLine)
    {
      inputLine.ArgumentNotNull("line");

      if (inputLine.Signifier >= 128)
      {
        return NullMarkupRule.Instance;
      }

      return _markupRules[inputLine.Signifier] ?? NullMarkupRule.Instance;
    }

    public bool IsAutoClosing(string tag)
    {
      tag.ArgumentNotEmpty("tag");

      return _autoClosingTags.Contains(tag.ToUpperInvariant());
    }

    public TemplateActivator<ICompiledTemplate> Compile(string templatePath, params Type[] genericArguments)
    {
      return Compile<ICompiledTemplate>(templatePath, genericArguments);
    }

    [SuppressMessage("Microsoft.Design", "CA1004")]
    public TemplateActivator<TView> Compile<TView>(string templatePath, params Type[] genericArguments)
    {
      return Compile<TView>(templatePath, null, genericArguments);
    }

    public TemplateActivator<ICompiledTemplate> Compile(string templatePath, string layoutPath, params Type[] genericArguments)
    {
      return Compile<ICompiledTemplate>(templatePath, layoutPath, genericArguments);
    }

    [SuppressMessage("Microsoft.Design", "CA1004")]
    public TemplateActivator<TView> Compile<TView>(string templatePath, string layoutPath, params Type[] genericArguments)
    {
      return Compile<TView>(templatePath, layoutPath, null, genericArguments);
    }

    public TemplateActivator<ICompiledTemplate> Compile(string templatePath, string layoutPath,
      ICollection<string> inputFiles, params Type[] genericArguments)
    {
      return Compile<ICompiledTemplate>(templatePath, layoutPath, inputFiles, genericArguments);
    }

    [SuppressMessage("Microsoft.Design", "CA1004")]
    public TemplateActivator<TView> Compile<TView>(string templatePath, string layoutPath,
      ICollection<string> inputFiles, params Type[] genericArguments)
    {
      templatePath.ArgumentNotEmpty("templatePath");
      templatePath.FileExists();

      if (!string.IsNullOrEmpty(layoutPath))
      {
        layoutPath.FileExists();
      }

      foreach (var type in genericArguments)
      {
        AddReferences(type);
      }

      var compilationContext
        = new CompilationContext(
          this,
          new TemplateClassBuilder(this, MakeClassName(templatePath), genericArguments),
          templatePath,
          layoutPath);

      Compile(compilationContext);

      if (inputFiles != null)
      {
        compilationContext.CollectInputFiles(inputFiles);
      }

      return CreateFastActivator<TView>(BuildView(compilationContext));
    }

    private void Compile(CompilationContext compilationContext)
    {
      while (compilationContext.CurrentNode.Next != null)
      {
        var rule = GetRule(compilationContext.CurrentInputLine);

        if (compilationContext.CurrentInputLine.IsMultiline && rule.MergeMultiline)
        {
          compilationContext.CurrentInputLine.Merge(compilationContext.NextInputLine);
          compilationContext.InputLines.Remove(compilationContext.NextNode);
        }
        else
        {
          rule.Process(compilationContext);
        }
      }

      compilationContext.CloseBlocks();
    }

    private Type BuildView(CompilationContext compilationContext)
    {
      var source = compilationContext.TemplateClassBuilder.Build();

      var typeBuilder = new TemplateTypeBuilder(this);

      var viewType = typeBuilder.Build(source, compilationContext.TemplateClassBuilder.ClassName);

      if (viewType == null)
      {
        ViewCompilationException.Throw(typeBuilder.CompilerResults,
          typeBuilder.Source, compilationContext.TemplatePath);
      }

      return viewType;
    }

    private static string MakeClassName(string templatePath)
    {
      return _pathCleaner.Replace(templatePath, "_").TrimStart('_');
    }

    private static TemplateActivator<TResult> CreateFastActivator<TResult>(Type type)
    {
      var dynamicMethod = new DynamicMethod("activatefast__", type, null, type);

      var ilGenerator = dynamicMethod.GetILGenerator();
      var constructor = type.GetConstructor(new Type[] {});

      if (constructor == null)
      {
        return null;
      }

      ilGenerator.Emit(OpCodes.Newobj, constructor);
      ilGenerator.Emit(OpCodes.Ret);

      return (TemplateActivator<TResult>)dynamicMethod.CreateDelegate(typeof(TemplateActivator<TResult>));
    }
  }
}