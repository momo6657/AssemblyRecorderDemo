package com.demo.picker;

import android.content.Intent;
import com.unity3d.player.UnityPlayerGameActivity;

public class PickerUnityActivity extends UnityPlayerGameActivity
{
    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data)
    {
        super.onActivityResult(requestCode, resultCode, data);
        PickerPlugin.onActivityResult(requestCode, resultCode, data);
    }
}
