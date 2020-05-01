---
layout: post
title: '[WPF] Using InputBindings with the MVVM pattern'
date: 2009-03-17T00:00:00.0000000
url: /2009/03/17/wpf-using-inputbindings-with-the-mvvm-pattern/
tags:
  - InputBinding
  - KeyBinding
  - markup extension
  - MVVM
  - WPF
  - XAML
categories:
  - Code sample
  - WPF
---

If you develop WPF applications according to the Model-View-ViewModel pattern, you may have faced this issue : in XAML, how to bind a key or mouse gesture to a ViewModel command ? The obvious and intuitive approach would be this one :  
```xml
    <UserControl.InputBindings>
        <KeyBinding Modifiers="Control" Key="E" Command="{Binding EditCommand}"/>
    </UserControl.InputBindings>
```
  Unfortunately, this code doesn't work, for two reasons : 
1. The `Command` property is not a dependency property, so you cannot assign it through binding
2. `InputBinding`s are not part of the logical or visual tree of the control, so they don't inherit the `DataContext`

  A solution would be to create the `InputBinding`s in the code-behind, but in the MVVM pattern we usually prefer to avoid this... I spent a long time looking for alternative solutions to do this in XAML, but most of them are quite complex and unintuitive. So I eventually came up with a markup extension that enables binding to ViewModel commands, anywhere in XAML, even for non-dependency properties or if the element doesn't normally inherit the `DataContext`  This extension is used like a regular binding :  
```xml
    <UserControl.InputBindings>
        <KeyBinding Modifiers="Control" Key="E" Command="{input:CommandBinding EditCommand}"/>
    </UserControl.InputBindings>
```
  (The *input* XML namespace is mapped to the CLR namespace where the markup extension is declared)  In order to write this extension, I had to cheat a little... I used Reflector to find some private fields that would allow to retrieve the `DataContext` of the root element. I then accessed those fields using reflection.  Here is the code of the markup extension :  
```csharp
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;

namespace MVVMLib.Input
{
    [MarkupExtensionReturnType(typeof(ICommand))]
    public class CommandBindingExtension : MarkupExtension
    {
        public CommandBindingExtension()
        {
        }

        public CommandBindingExtension(string commandName)
        {
            this.CommandName = commandName;
        }

        [ConstructorArgument("commandName")]
        public string CommandName { get; set; }

        private object targetObject;
        private object targetProperty;

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            IProvideValueTarget provideValueTarget = serviceProvider.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
            if (provideValueTarget != null)
            {
                targetObject = provideValueTarget.TargetObject;
                targetProperty = provideValueTarget.TargetProperty;
            }

            if (!string.IsNullOrEmpty(CommandName))
            {
                // The serviceProvider is actually a ProvideValueServiceProvider, which has a private field "_context" of type ParserContext
                ParserContext parserContext = GetPrivateFieldValue<ParserContext>(serviceProvider, "_context");
                if (parserContext != null)
                {
                    // A ParserContext has a private field "_rootElement", which returns the root element of the XAML file
                    FrameworkElement rootElement = GetPrivateFieldValue<FrameworkElement>(parserContext, "_rootElement");
                    if (rootElement != null)
                    {
                        // Now we can retrieve the DataContext
                        object dataContext = rootElement.DataContext;

                        // The DataContext may not be set yet when the FrameworkElement is first created, and it may change afterwards,
                        // so we handle the DataContextChanged event to update the Command when needed
                        if (!dataContextChangeHandlerSet)
                        {
                            rootElement.DataContextChanged += new DependencyPropertyChangedEventHandler(rootElement_DataContextChanged);
                            dataContextChangeHandlerSet = true;
                        }

                        if (dataContext != null)
                        {
                            ICommand command = GetCommand(dataContext, CommandName);
                            if (command != null)
                                return command;
                        }
                    }
                }
            }

            // The Command property of an InputBinding cannot be null, so we return a dummy extension instead
            return DummyCommand.Instance;
        }

        private ICommand GetCommand(object dataContext, string commandName)
        {
            PropertyInfo prop = dataContext.GetType().GetProperty(commandName);
            if (prop != null)
            {
                ICommand command = prop.GetValue(dataContext, null) as ICommand;
                if (command != null)
                    return command;
            }
            return null;
        }

        private void AssignCommand(ICommand command)
        {
            if (targetObject != null && targetProperty != null)
            {
                if (targetProperty is DependencyProperty)
                {
                    DependencyObject depObj = targetObject as DependencyObject;
                    DependencyProperty depProp = targetProperty as DependencyProperty;
                    depObj.SetValue(depProp, command);
                }
                else
                {
                    PropertyInfo prop = targetProperty as PropertyInfo;
                    prop.SetValue(targetObject, command, null);
                }
            }
        }

        private bool dataContextChangeHandlerSet = false;
        private void rootElement_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            FrameworkElement rootElement = sender as FrameworkElement;
            if (rootElement != null)
            {
                object dataContext = rootElement.DataContext;
                if (dataContext != null)
                {
                    ICommand command = GetCommand(dataContext, CommandName);
                    if (command != null)
                    {
                        AssignCommand(command);
                    }
                }
            }
        }

        private T GetPrivateFieldValue<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null)
            {
                return (T)field.GetValue(target);
            }
            return default(T);
        }

        // A dummy command that does nothing...
        private class DummyCommand : ICommand
        {

            #region Singleton pattern

            private DummyCommand()
            {
            }

            private static DummyCommand _instance = null;
            public static DummyCommand Instance
            {
                get
                {
                    if (_instance == null)
                    {
                        _instance = new DummyCommand();
                    }
                    return _instance;
                }
            }

            #endregion

            #region ICommand Members

            public bool CanExecute(object parameter)
            {
                return false;
            }

            public event EventHandler CanExecuteChanged;

            public void Execute(object parameter)
            {
            }

            #endregion
        }
    }
}
```
  However this solution has a limitation : it works only for the `DataContext` of the XAML root. So you can't use it, for instance, to define an InputBinding on a control whose `DataContext` is also redefined, because the markup extension will access the root `DataContext`. It shouldn't be a problem in most cases, but you need to be aware of that...

