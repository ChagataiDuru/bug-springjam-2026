using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace OrangeWolf.Generic
{
    public sealed class TsvParser
    {
        private string _newLine = "\n";
        private readonly char _tsvSeparator = '\t';
        
        public List<T> Parse<T>(string tsv) where T : class
        {
            if (string.IsNullOrEmpty(tsv))
                return null;
            
            if(IsFileCrLf(tsv))
                _newLine = '\r' + _newLine;
            
            string[] lines = tsv.Split(_newLine);
            if (lines.Length <= 1)
                return null;
            
            string[] headers = lines[0].Split(_tsvSeparator);
            if (headers.Length <= 1)
                return null;

            /*
            T lineDataA = Activator.CreateInstance<T>();
            Type dataTypeA = lineDataA.GetType();
            const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.Instance;
            MemberInfo[] members = dataTypeA.GetFields(bindingFlags).Cast<MemberInfo>()
                .Concat(dataTypeA.GetProperties(bindingFlags)).ToArray();

            foreach (var memberInfo in members)
            {
                Debug.Log($"Name: {memberInfo.Name}. MemberType: {memberInfo.MemberType}");
            }
            */
            
            List<T> allData = new List<T>();
            
            for (int i = 1; i < lines.Length; i++)
            {
                T lineData = Activator.CreateInstance<T>();
                Type dataType = lineData.GetType();
                
                string[] values = lines[i].Split(_tsvSeparator);
                if (values.Length <= 1)
                    continue;

                if (string.IsNullOrEmpty(values[0]))
                {
                    Debug.LogError($"Empty value at line {i}! Skipping...");
                    continue;
                }
                
                for (int j = 0; j < headers.Length; j++)
                {
                    string header = headers[j].ToString(CultureInfo.InvariantCulture);
                    string value = values[j];
                    
                    var field = dataType.GetField(header);
                    if (field != null)
                    {
                        field.SetValue(lineData, value);
                    }
                    else
                    {
                        var property = dataType.GetProperty(header);
                        if (property != null)
                        {
                            if(property.PropertyType == typeof(int))
                                property.SetValue(lineData, Convert.ToInt32(value));
                            else if(property.PropertyType == typeof(long))
                                property.SetValue(lineData, Convert.ToInt64(value));
                            else if(property.PropertyType == typeof(float))
                                property.SetValue(lineData, Convert.ToSingle(value));
                            else if(property.PropertyType == typeof(double))
                                property.SetValue(lineData, Convert.ToDouble(value));
                            else if(property.PropertyType == typeof(bool))
                                property.SetValue(lineData, Convert.ToBoolean(value));
                            else if(property.PropertyType == typeof(string))
                                property.SetValue(lineData, value);
                            else if(property.PropertyType == typeof(DateTime))
                                property.SetValue(lineData, Convert.ToDateTime(value));
                            else if(property.PropertyType == typeof(TimeSpan))
                                property.SetValue(lineData, TimeSpan.Parse(value));
                            else if (property.PropertyType == typeof(Enum))
                                property.SetValue(lineData, Enum.Parse(property.PropertyType, value));
                            else if (property.PropertyType.BaseType == typeof(Enum))
                                property.SetValue(lineData, Enum.Parse(property.PropertyType, value));
                            else
                                throw new Exception($"TsvParser: Type {property.PropertyType} not supported! Value: {value}");
                        }
                        else
                        {
                            Debug.LogError($"Field or Property not found. Name: {header}");
                        }
                    }
                }
                
                allData.Add(lineData);
            }
            
            return allData;
        }
        
        private bool IsFileCrLf(string file)
        {
            return file.Contains("\r\n");
        }
        
        public (string[], List<string[]>) Parse(string tsv)
        {
            if (string.IsNullOrEmpty(tsv))
                return (null, null);
            
            if(IsFileCrLf(tsv))
                _newLine = '\r' + _newLine;
            
            string[] lines = tsv.Split(_newLine);
            if (lines.Length <= 1)
                return (null, null);
            
            string[] headers = lines[0].Split(_tsvSeparator);
            if (headers.Length <= 1)
                return (null, null);
            
            List<string[]> allData = new List<string[]>();
            
            for (int i = 1; i < lines.Length; i++)
            {
                string[] values = lines[i].Split(_tsvSeparator);
                if (values.Length <= 1)
                    continue;

                allData.Add(values);
            }
            
            return (headers, allData);
        }
    }
}