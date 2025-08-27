# CallREC-Scribe

## 调试

因为vs中的调试器会阻止回调的执行，所以如果你在调试器中运行程序，FFmpeg的回调函数将不会被调用。程序卡在等待状态。
如果想获得调试输出，请用
```
adb logcat -s "com.tokisaki.CallREC_Scribe" "*:S"
```