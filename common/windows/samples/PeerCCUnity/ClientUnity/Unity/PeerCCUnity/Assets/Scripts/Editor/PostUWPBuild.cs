using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

public class PostUWPBuild
{
    [PostProcessBuild(1)]
    public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if(target != BuildTarget.WSAPlayer) return;

        Debug.Log($"Running post UWP build script at build path {pathToBuiltProject}...");

        // there should be only one
        var appxmanifest = Directory.GetFiles(pathToBuiltProject, "*.appxmanifest", SearchOption.AllDirectories)[0];

        Debug.Log($"Found .appxmanifest file at {appxmanifest}...");

        var lines = File.ReadAllLines(appxmanifest).ToList();

        if (lines.Any(x => x.Contains("WebRtcScheme.dll")))
        {
            Debug.Log(".appxmanifest already contains WebRtcScheme activatable class, nothing to do.");
        }
        else
        {
            var last = lines.Last();
            lines.RemoveAt(lines.Count-1);
            lines.Add("  <Extensions>");
            lines.Add("    <Extension Category=\"windows.activatableClass.inProcessServer\">");
            lines.Add("      <InProcessServer>");
            lines.Add("        <Path>WebRtcScheme.dll</Path>");
            lines.Add("        <ActivatableClass ActivatableClassId=\"WebRtcScheme.SchemeHandler\" ThreadingModel=\"both\" />");
            lines.Add("      </InProcessServer>");
            lines.Add("    </Extension>");
            lines.Add("  </Extensions>");
            lines.Add(last);
            File.WriteAllLines(appxmanifest, lines);
            Debug.Log("Successfully added WebRtcScheme activatable class to .appxmanifest!");
        }
    }
}
