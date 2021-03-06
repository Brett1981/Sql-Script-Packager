﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using SqlScriptPackager.Core;
using System.Collections.ObjectModel;
using SqlScriptPackager.WPF.DisplayWrappers;
using System.Collections;
using Microsoft.Win32;
using SqlScriptPackager.Core.Packaging;
using System.Configuration;
using SqlScriptPackager.WPF.Properties;

namespace SqlScriptPackager.WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string SCRIPT_PACKAGE_FILTER = "Script Package (*.sp)|*.sp";

        private static OpenFileDialog _sqlFileDialog = new OpenFileDialog();
        private DatabaseConnection _defaultDatabaseConnection;

        #region Properties
        public DatabaseConnection DefaultDatabaseConnection
        {
            get { return _defaultDatabaseConnection; }
            set { _defaultDatabaseConnection = value; DefaultDatabaseConnection_Changed(); }
        }
        
        public Collection<DatabaseConnection> DatabaseConnections
        {
            get;
            protected set;
        }

        public ObservableCollection<ScriptWrapper> Scripts
        {
            get;
            protected set;
        }

        public ScriptProviderCollection CustomScriptProviders
        {
            get { return ScriptProviderManager.Providers; }
        }

        public ScriptProvider SelectedCustomScriptProvider
        {
            get;
            set;
        }

        public IList SelectedScripts
        {
            get { return scriptListView.SelectedItems; }
        }
        #endregion

        public MainWindow()
        {
            _sqlFileDialog.Filter = "Sql Scripts (*.sql)|*.sql";
            _sqlFileDialog.CheckFileExists = true;
            _sqlFileDialog.Multiselect = true;

            Scripts = new ObservableCollection<ScriptWrapper>();
            LoadConnectionStrings();
            SelectDefaultScriptProvider();
         
            InitializeComponent();

            this.Title = string.Concat(this.Title, " (", App.GetProgramVersion(), ")");
        }

        private void SelectDefaultScriptProvider()
        {
            if (string.IsNullOrWhiteSpace(Settings.Default.DefaultScriptProvider))
                return;

            foreach (ScriptProvider provider in CustomScriptProviders)
            {
                if (provider.GetType().ToString() != Settings.Default.DefaultScriptProvider)
                    continue;

                SelectedCustomScriptProvider = provider;
                break;
            }
        }

        private void LoadConnectionStrings()
        {
            DatabaseConnections = new Collection<DatabaseConnection>();

            foreach (ConnectionStringSettings settings in ConfigurationManager.ConnectionStrings)
                this.DatabaseConnections.Add(new DatabaseConnection(settings.Name, settings.ConnectionString));
            
            this.DefaultDatabaseConnection = this.DatabaseConnections[0];
        }

        private void AddScripts(IEnumerable<Script> scriptsToAdd)
        {
            if (scriptsToAdd == null)
                return;

            foreach (Script script in scriptsToAdd)
                Scripts.Add(new ScriptWrapper(script));
        }

        private void RemoveSelectedScripts()
        {
            while (SelectedScripts.Count > 0)
                Scripts.Remove(SelectedScripts[0] as ScriptWrapper);
        }

        private void DefaultDatabaseConnection_Changed()
        {
            if (Scripts.Count == 0 || SelectedScripts.Count < 2)
                return;

            foreach (ScriptWrapper wrapper in SelectedScripts)
                wrapper.Connection = DefaultDatabaseConnection;
        }

        protected override void OnClosed(EventArgs e)
        {
            if (SelectedCustomScriptProvider != null)
            {
                Settings.Default.DefaultScriptProvider = SelectedCustomScriptProvider.GetType().ToString();
                Settings.Default.Save();
            }

            base.OnClosed(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
                RemoveSelectedScripts();

            base.OnKeyDown(e);
        }

        #region Commands
        private void MoveScript_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            int direction = int.Parse(e.Parameter as string);
            var indiciesToMove = (from ScriptWrapper wrapper in SelectedScripts
                                    orderby Scripts.IndexOf(wrapper)
                                    select Scripts.IndexOf(wrapper));

            if (direction > 0)
                 indiciesToMove = indiciesToMove.Reverse();

            int ceilingIndex = -1;
            foreach(int curIndex in indiciesToMove)
            {
                if (curIndex + direction >= Scripts.Count || curIndex + direction < 0 || curIndex + direction == ceilingIndex)
                {
                    ceilingIndex = curIndex;
                    continue;
                }

                Scripts.Move(curIndex, curIndex + direction);
            }
        }

        private void MoveScript_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = SelectedScripts.Count > 0;
        }

        private void AddScript_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (_sqlFileDialog.ShowDialog() != true)
                return;
            
            foreach (string file in _sqlFileDialog.FileNames)
                Scripts.Add(new ScriptWrapper(new SqlScript(new DiskScriptResource(file), DefaultDatabaseConnection)));
        }

        private void DeleteScripts_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = SelectedScripts.Count > 0;
        }

        private void DeleteScripts_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            RemoveSelectedScripts();
        }

        private void SaveScriptPackage_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = Scripts.Count > 0;
        }

        private void SaveScriptPackage_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            SaveFileDialog packageSave = new SaveFileDialog();
            packageSave.CheckFileExists = false;
            packageSave.Filter = SCRIPT_PACKAGE_FILTER;
            packageSave.AddExtension = true;
            packageSave.DefaultExt = "sp";

            if (packageSave.ShowDialog() != true)
                return;

            ScriptPackage package = new ScriptPackage();
            var scripts = (from ScriptWrapper wrapper in Scripts
                           select wrapper.Script);

            package.Scripts.Add(scripts);
            package.Save(packageSave.FileName);
        }

        private void LoadScriptPackage_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            OpenFileDialog packageLoad = new OpenFileDialog();
            packageLoad.Filter = SCRIPT_PACKAGE_FILTER;
            packageLoad.CheckFileExists = true;

            if (packageLoad.ShowDialog() != true)
                return;

            ScriptPackage package = new ScriptPackage();
            package.Load(packageLoad.FileName);

            this.Scripts.Clear();
            foreach (Script script in package.Scripts)
            {
                DatabaseConnection matchingConnection = (from connection in DatabaseConnections
                                                         where connection.ConnectionString == script.Connection.ConnectionString
                                                         select connection).FirstOrDefault();

                if (matchingConnection == null)
                {
                    matchingConnection = new DatabaseConnection("[PACKAGE] " + script.Connection.ConnectionName, script.Connection.ConnectionString);
                    this.DatabaseConnections.Add(matchingConnection);
                }

                script.Connection = matchingConnection;
                this.Scripts.Add(new ScriptWrapper(script));
            }
        }

        private void ViewConnectionInfo_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            ViewConnectionInfo infoWindow = new ViewConnectionInfo(this.DatabaseConnections);
            infoWindow.ShowDialog();
        }

        private void ExecuteScripts_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = this.Scripts.Count > 0;
        }

        private void ExecuteScripts_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            foreach (ScriptWrapper wrapper in this.Scripts)
                wrapper.Script.ResetScript();

            ExecutionWindow window = new ExecutionWindow(this.Scripts);
            window.ShowDialog();
        }

        private void Close_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            this.Close();
        }
        #endregion

        private void AddCustomScript_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = this.SelectedCustomScriptProvider != null;
        }

        private void AddCustomScript_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            AddScripts(SelectedCustomScriptProvider.GetTasks(this.DefaultDatabaseConnection));
        }
    }

    public static class Commands
    {
        public static RoutedUICommand MoveScripts = new RoutedUICommand();
        public static RoutedUICommand AddScript = new RoutedUICommand();
        public static RoutedUICommand AddCustomScript = new RoutedUICommand();
        public static RoutedUICommand DeleteScripts = new RoutedUICommand();
        public static RoutedUICommand SaveScriptPackage = new RoutedUICommand();
        public static RoutedUICommand LoadScriptPackage = new RoutedUICommand();
        public static RoutedUICommand ViewConnectionInfo = new RoutedUICommand();
        public static RoutedUICommand ExecuteScripts = new RoutedUICommand();
    }    
}
