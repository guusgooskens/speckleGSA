#pragma checksum "..\..\MainWindow.xaml" "{ff1816ec-aa5e-4d10-87f7-6f4963833460}" "FB11E8E844CDC8C95B522142D3FC69AE0231B471"
//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

using SpeckleGSAUI;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Media.TextFormatting;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Shell;


namespace SpeckleGSAUI
{


    /// <summary>
    /// MainWindow
    /// </summary>
    public partial class MainWindow : System.Windows.Window, System.Windows.Markup.IComponentConnector
    {


#line 33 "..\..\MainWindow.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.TabControl TabControl;

#line default
#line hidden


#line 41 "..\..\MainWindow.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.TextBox ServerAddress;

#line default
#line hidden


#line 47 "..\..\MainWindow.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.TextBox EmailAddress;

#line default
#line hidden


#line 53 "..\..\MainWindow.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.PasswordBox Password;

#line default
#line hidden


#line 90 "..\..\MainWindow.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.TextBox SenderStreamID;

#line default
#line hidden


#line 95 "..\..\MainWindow.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.CheckBox ToggleSender;

#line default
#line hidden


#line 99 "..\..\MainWindow.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.CheckBox SendDesignLayer;

#line default
#line hidden


#line 102 "..\..\MainWindow.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.CheckBox SendAnalysisLayer;

#line default
#line hidden


#line 115 "..\..\MainWindow.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.TextBox ReceiverStreamName;

#line default
#line hidden


#line 121 "..\..\MainWindow.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.TextBox ReceiverStreamID;

#line default
#line hidden


#line 126 "..\..\MainWindow.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.CheckBox ToggleReceiver;

#line default
#line hidden


#line 138 "..\..\MainWindow.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.TextBlock Messages;

#line default
#line hidden

        private bool _contentLoaded;

        /// <summary>
        /// InitializeComponent
        /// </summary>
        [System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [System.CodeDom.Compiler.GeneratedCodeAttribute("PresentationBuildTasks", "4.0.0.0")]
        public void InitializeComponent()
        {
            if (_contentLoaded)
            {
                return;
            }
            _contentLoaded = true;
            System.Uri resourceLocater = new System.Uri("/SpeckleGSAUI;component/mainwindow.xaml", System.UriKind.Relative);

#line 1 "..\..\MainWindow.xaml"
            System.Windows.Application.LoadComponent(this, resourceLocater);

#line default
#line hidden
        }

        [System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [System.CodeDom.Compiler.GeneratedCodeAttribute("PresentationBuildTasks", "4.0.0.0")]
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)]
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1800:DoNotCastUnnecessarily")]
        void System.Windows.Markup.IComponentConnector.Connect(int connectionId, object target)
        {
            switch (connectionId)
            {
                case 1:
                    this.TabControl = ((System.Windows.Controls.TabControl)(target));
                    return;
                case 2:
                    this.ServerAddress = ((System.Windows.Controls.TextBox)(target));
                    return;
                case 3:
                    this.EmailAddress = ((System.Windows.Controls.TextBox)(target));
                    return;
                case 4:
                    this.Password = ((System.Windows.Controls.PasswordBox)(target));
                    return;
                case 5:

#line 56 "..\..\MainWindow.xaml"
                    ((System.Windows.Controls.Button)(target)).Click += new System.Windows.RoutedEventHandler(this.Login);

#line default
#line hidden
                    return;
                case 6:

#line 64 "..\..\MainWindow.xaml"
                    ((System.Windows.Controls.Button)(target)).Click += new System.Windows.RoutedEventHandler(this.LinkGSA);

#line default
#line hidden
                    return;
                case 7:

#line 68 "..\..\MainWindow.xaml"
                    ((System.Windows.Controls.Button)(target)).Click += new System.Windows.RoutedEventHandler(this.NewGSAFile);

#line default
#line hidden
                    return;
                case 8:

#line 72 "..\..\MainWindow.xaml"
                    ((System.Windows.Controls.Button)(target)).Click += new System.Windows.RoutedEventHandler(this.OpenGSAFile);

#line default
#line hidden
                    return;
                case 9:
                    this.SenderStreamID = ((System.Windows.Controls.TextBox)(target));
                    return;
                case 10:
                    this.ToggleSender = ((System.Windows.Controls.CheckBox)(target));

#line 93 "..\..\MainWindow.xaml"
                    this.ToggleSender.Checked += new System.Windows.RoutedEventHandler(this.SenderOn);

#line default
#line hidden

#line 94 "..\..\MainWindow.xaml"
                    this.ToggleSender.Unchecked += new System.Windows.RoutedEventHandler(this.SenderOff);

#line default
#line hidden
                    return;
                case 11:
                    this.SendDesignLayer = ((System.Windows.Controls.CheckBox)(target));
                    return;
                case 12:
                    this.SendAnalysisLayer = ((System.Windows.Controls.CheckBox)(target));
                    return;
                case 13:
                    this.ReceiverStreamName = ((System.Windows.Controls.TextBox)(target));

#line 114 "..\..\MainWindow.xaml"
                    this.ReceiverStreamName.TextChanged += new System.Windows.Controls.TextChangedEventHandler(this.UpdateStreamName);

#line default
#line hidden
                    return;
                case 14:
                    this.ReceiverStreamID = ((System.Windows.Controls.TextBox)(target));
                    return;
                case 15:
                    this.ToggleReceiver = ((System.Windows.Controls.CheckBox)(target));

#line 124 "..\..\MainWindow.xaml"
                    this.ToggleReceiver.Checked += new System.Windows.RoutedEventHandler(this.ReceiverOn);

#line default
#line hidden

#line 125 "..\..\MainWindow.xaml"
                    this.ToggleReceiver.Unchecked += new System.Windows.RoutedEventHandler(this.ReceiverOff);

#line default
#line hidden
                    return;
                case 16:
                    this.Messages = ((System.Windows.Controls.TextBlock)(target));
                    return;
            }
            this._contentLoaded = true;
        }

        internal System.Windows.Controls.TextBox SenderStreamName;
    }
}

