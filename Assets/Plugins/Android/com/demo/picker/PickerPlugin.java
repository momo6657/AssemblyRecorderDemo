package com.demo.picker;

import android.app.Activity;
import android.content.ContentResolver;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.provider.OpenableColumns;
import android.util.Log;

import com.unity3d.player.UnityPlayer;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;

public class PickerPlugin {

    private static final int REQ_CODE = 4242;
    private static String unityObjectName = "AndroidPickerBridge";
    private static String unityCallbackMethod = "OnFilePicked";

    public static void setUnityReceiver(String gameObjectName, String methodName) {
        unityObjectName = gameObjectName;
        unityCallbackMethod = methodName;
    }

    public static void openDocumentPicker() {
        Activity act = UnityPlayer.currentActivity;
        try {
            Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
            intent.addCategory(Intent.CATEGORY_OPENABLE);
            intent.setType("*/*");
            intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
            intent.addFlags(Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);
            act.startActivityForResult(intent, REQ_CODE);
        } catch (Exception e) {
            Log.e("PickerPlugin", "openDocumentPicker failed", e);
            UnityPlayer.UnitySendMessage(unityObjectName, unityCallbackMethod, "");
        }
    }

    // Unity 的 Activity 会通过反射把 onActivityResult 分发到插件类
    public static void onActivityResult(int requestCode, int resultCode, Intent data) {
        if (requestCode != REQ_CODE) return;

        String resultPath = "";

        try {
            if (resultCode == Activity.RESULT_OK && data != null) {
                Uri uri = data.getData();
                if (uri != null) {

                    Activity act = UnityPlayer.currentActivity;

                    // 持久化权限（有些机型/系统需要）
                    final int takeFlags = data.getFlags()
                            & (Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
                    try {
                        act.getContentResolver().takePersistableUriPermission(uri, takeFlags);
                    } catch (Exception ignore) {}

                    // ✅ 关键：拷贝到 app 私有目录，并返回真实路径
                    resultPath = copyUriToAppFile(act, uri);
                }
            }
        } catch (Exception e) {
            Log.e("PickerPlugin", "onActivityResult copy failed", e);
            resultPath = "";
        }

        // ✅ 回传真实文件路径（不是 content://）
        UnityPlayer.UnitySendMessage(unityObjectName, unityCallbackMethod, resultPath);
    }

    private static String guessExtFromUri(ContentResolver cr, Uri uri) {
        String name = null;
        Cursor c = cr.query(uri, null, null, null, null);
        if (c != null) {
            int idx = c.getColumnIndex(OpenableColumns.DISPLAY_NAME);
            if (c.moveToFirst() && idx >= 0) name = c.getString(idx);
            c.close();
        }
        if (name != null) {
            String lower = name.toLowerCase();
            if (lower.endsWith(".gltf")) return ".gltf";
            if (lower.endsWith(".glb")) return ".glb";
        }
        return ".glb";
    }

    private static String copyUriToAppFile(Activity act, Uri uri) throws Exception {
        ContentResolver cr = act.getContentResolver();

        String ext = guessExtFromUri(cr, uri);
        String fileName = "import_" + System.currentTimeMillis() + ext;

        // ✅ app 私有目录（Android/data/包名/files/）
        File dir = act.getExternalFilesDir(null);
        if (dir == null) throw new Exception("getExternalFilesDir returned null");

        File outFile = new File(dir, fileName);

        InputStream is = cr.openInputStream(uri);
        if (is == null) throw new Exception("openInputStream returned null");

        FileOutputStream os = new FileOutputStream(outFile);

        byte[] buf = new byte[1024 * 64];
        int n;
        while ((n = is.read(buf)) > 0) {
            os.write(buf, 0, n);
        }

        os.flush();
        os.close();
        is.close();

        Log.i("PickerPlugin", "Copied to: " + outFile.getAbsolutePath());
        return outFile.getAbsolutePath();
    }
}