using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;

namespace HUDRA.Services.FanControl
{
    /// <summary>
    /// Utility class for Windows Management Instrumentation (WMI) operations.
    /// Provides helper methods for calling WMI methods with parameters.
    /// </summary>
    public static class WmiHelper
    {
        /// <summary>
        /// Calls a WMI method without expecting a return value.
        /// </summary>
        /// <param name="scope">WMI namespace (e.g., "root\\WMI")</param>
        /// <param name="query">WQL query to find the WMI object</param>
        /// <param name="methodName">Name of the method to invoke</param>
        /// <param name="methodParams">Dictionary of parameter names and values</param>
        /// <returns>True if the call succeeded, false otherwise</returns>
        public static bool Call(
            string scope,
            string query,
            string methodName,
            Dictionary<string, object> methodParams)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(scope, query);
                using var collection = searcher.Get();

                var managementObject = collection.Cast<ManagementObject>().FirstOrDefault();
                if (managementObject == null)
                {
                    Debug.WriteLine($"WMI object not found for query: {query}");
                    return false;
                }

                using (managementObject)
                {
                    // Get method parameters
                    using var inParams = managementObject.GetMethodParameters(methodName);

                    // Populate parameters
                    foreach (var param in methodParams)
                    {
                        inParams[param.Key] = param.Value;
                    }

                    // Invoke the method
                    using var outParams = managementObject.InvokeMethod(methodName, inParams, null);

                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WMI call failed - Method: {methodName}, Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Calls a WMI method and returns a transformed result.
        /// </summary>
        /// <typeparam name="T">Type of the result to return</typeparam>
        /// <param name="scope">WMI namespace (e.g., "root\\WMI")</param>
        /// <param name="query">WQL query to find the WMI object</param>
        /// <param name="methodName">Name of the method to invoke</param>
        /// <param name="methodParams">Dictionary of parameter names and values</param>
        /// <param name="resultSelector">Function to transform the result properties to type T</param>
        /// <returns>The transformed result, or default(T) if the call fails</returns>
        public static T? Call<T>(
            string scope,
            string query,
            string methodName,
            Dictionary<string, object> methodParams,
            Func<PropertyDataCollection, T> resultSelector)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(scope, query);
                using var collection = searcher.Get();

                var managementObject = collection.Cast<ManagementObject>().FirstOrDefault();
                if (managementObject == null)
                {
                    Debug.WriteLine($"WMI object not found for query: {query}");
                    return default;
                }

                using (managementObject)
                {
                    // Get method parameters
                    using var inParams = managementObject.GetMethodParameters(methodName);

                    // Populate parameters
                    foreach (var param in methodParams)
                    {
                        inParams[param.Key] = param.Value;
                    }

                    // Invoke the method
                    using var outParams = managementObject.InvokeMethod(methodName, inParams, null);

                    if (outParams == null)
                    {
                        Debug.WriteLine($"WMI method returned null: {methodName}");
                        return default;
                    }

                    // Transform and return the result
                    return resultSelector(outParams.Properties);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WMI call failed - Method: {methodName}, Error: {ex.Message}");
                return default;
            }
        }
    }
}
