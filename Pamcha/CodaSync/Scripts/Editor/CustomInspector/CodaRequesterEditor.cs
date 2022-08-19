using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Com.Pamcha.CodaSync {
    [CustomEditor(typeof(CodaRequester))]
    public class CodaRequesterEditor : Editor {
        private MessageType messageType = MessageType.Info;
        private string connectionTestResult;

        public override void OnInspectorGUI() {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "_apiToken");

            SerializedProperty property = serializedObject.FindProperty("_apiToken");
            property.stringValue = EditorGUILayout.PasswordField("API key", property.stringValue);

            bool test = GUILayout.Button("Test connection");

            if (test)
                TestConnection();

            if (!string.IsNullOrEmpty(connectionTestResult))
                EditorGUILayout.HelpBox(connectionTestResult, messageType);

            serializedObject.ApplyModifiedProperties();
        }

        private void TestConnection() {
            messageType = MessageType.Info;
            connectionTestResult = "Waiting for response...";
            ((CodaRequester)target).PerformConnectionTest(DisplayResult);
        }

        private void DisplayResult(UnityWebRequest req) {
            connectionTestResult = $"Response : {req.result}";

            if (req.result == UnityWebRequest.Result.Success) {
                connectionTestResult = $"{connectionTestResult}\n{req.downloadHandler.text}";
                messageType = MessageType.Info;
            } else {
                connectionTestResult = $"{connectionTestResult}\nResponse Code : {req.responseCode}\n{req.error}";
                messageType = MessageType.Error;
            }
        }
    }
}
