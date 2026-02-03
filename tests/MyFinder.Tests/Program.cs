using System;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using MyFinder;

namespace MyFinder.Tests
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Console.WriteLine("Starting Test harness...");

            // 1. Create App context (needed for Resources)
            var app = new Application();
            
            // 2. Instantiate MainWindow
            Console.WriteLine("Creating MainWindow...");
            var window = new MainWindow();
            
            // We need to trigger Loaded to ensure resources are loaded? 
            // Actually Resources are in App.xaml or Window.xaml.
            // InitializeComponent called in ctor handles Window resources.
            
            // 3. Get Reference to LstFiles (ListView)
            // Use Reflection to get field
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var lstFilesField = typeof(MainWindow).GetField("LstFiles", flags);
            if (lstFilesField == null)
            {
                 Console.WriteLine("ERROR: Could not find LstFiles field. Has it been renamed?");
                 Environment.Exit(1);
            }
            var lstFiles = (ListView)lstFilesField.GetValue(window)!;

            // 4. Get Reference to BtnViewToggle_Click
            var clickMethod = typeof(MainWindow).GetMethod("BtnViewToggle_Click", flags);
             if (clickMethod == null)
            {
                 Console.WriteLine("ERROR: Could not find BtnViewToggle_Click method.");
                 Environment.Exit(1);
            }

            Console.WriteLine("Initial State: ItemTemplate should be set (Card View).");
            if (lstFiles.ItemTemplate == null)
            {
                 Console.WriteLine("WARNING: Initial ItemTemplate is null. Expected CardViewTemplate.");
                 // Proceed anyway
            }
            else 
            {
                 Console.WriteLine("Verified: Initial ItemTemplate is present.");
            }

            // 5. Simulate CLICK (Switch to Grid View)
            Console.WriteLine(">>> Simulating Click 1 (Switch to Grid View)...");
            clickMethod.Invoke(window, new object[] { null, null });

            // 6. Verify Crash didn't happen (if we are here, good) and State
            // Check ItemTemplate is NULL
            if (lstFiles.ItemTemplate != null)
            {
                Console.WriteLine("FAILURE: ItemTemplate should be NULL in Grid View to avoid crash!");
                Environment.Exit(2);
            }
            else
            {
                Console.WriteLine("SUCCESS: ItemTemplate is NULL in Grid View.");
            }

            // Check View is GridView
            if (lstFiles.View is GridView)
            {
                 Console.WriteLine("SUCCESS: View is GridView.");
            }
            else
            {
                 Console.WriteLine($"FAILURE: View should be GridView, but is {lstFiles.View?.GetType().Name ?? "null"}");
                 Environment.Exit(3);
            }

            // 7. Simulate CLICK (Switch back to Icon View)
            Console.WriteLine(">>> Simulating Click 2 (Switch back to Icon View)...");
            clickMethod.Invoke(window, new object[] { null, null });

            // 8. Verify
             if (lstFiles.ItemTemplate == null)
            {
                Console.WriteLine("FAILURE: ItemTemplate should be RESTORED in Icon View!");
                Environment.Exit(4);
            }
            else
            {
                Console.WriteLine("SUCCESS: ItemTemplate is RESTORED.");
            }

            Console.WriteLine("ALL TESTS PASSED.");
            Environment.Exit(0);
        }
    }
}
