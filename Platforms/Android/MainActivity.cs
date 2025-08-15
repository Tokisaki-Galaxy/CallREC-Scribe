using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using AndroidX.DocumentFile.Provider;

namespace CallREC_Scribe;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    // 用于保存异步操作的“承诺”，以便在未来的某个时间点完成它
    internal static TaskCompletionSource<string> PickFolderTaskCompletionSource { set; get; }

    // 定义一个唯一的请求码，用于识别是哪个请求返回了结果
    internal const int PickFolderRequestCode = 1234;

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        // 检查是不是我们发出的文件夹选择请求返回了结果
        if (requestCode == PickFolderRequestCode)
        {
            // 检查用户是否成功选择了一个文件夹 (而不是按了返回或取消)
            if (resultCode == Result.Ok && data?.Data != null)
            {
                var uri = data.Data;

                // 关键一步：获取对这个文件夹的持久化读取权限
                // 这样即使用户重启了手机，我们下次依然能访问这个文件夹
                ContentResolver.TakePersistableUriPermission(uri, ActivityFlags.GrantReadUriPermission);

                // 将 content:// URI 转换为一个真实的文件系统路径
                string path = GetPathFromTreeUri(this, uri);

                // 完成我们的异步任务，并把路径作为结果返回
                PickFolderTaskCompletionSource?.TrySetResult(path);
            }
            else
            {
                // 如果用户取消了选择，我们也需要完成任务，返回 null
                PickFolderTaskCompletionSource?.TrySetResult(null);
            }
        }
    }

    // 这是一个辅助方法，用于将 Android 的 content:// URI 转换为我们 C# 代码能用的标准文件路径
    // 这段代码比较固定，可以直接使用
    private static string GetPathFromTreeUri(Context context, Android.Net.Uri treeUri)
    {
        var docId = DocumentsContract.GetTreeDocumentId(treeUri);
        var split = docId.Split(':');
        if (split.Length > 1 && split[0].Equals("primary", StringComparison.OrdinalIgnoreCase))
        {
            var path = split[1];
            return Path.Combine(Android.OS.Environment.ExternalStorageDirectory.Path, path);
        }
        return null;
    }
}