package com.example.app

import android.widget.Toast
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job

open class BaseActivity : AppCompatActivity() {
    protected val scopeJob = Job()
    protected val scope = CoroutineScope(Dispatchers.Main + scopeJob)

    protected fun canShowUi(): Boolean = !(isFinishing || isDestroyed)

    protected fun safeToast(text: String, long: Boolean = false) {
        if (!canShowUi()) return
        Toast.makeText(this, text, if (long) Toast.LENGTH_LONG else Toast.LENGTH_SHORT).show()
    }

    protected fun safeShowDialog(builder: AlertDialog.Builder): AlertDialog? {
        if (!canShowUi()) return null
        return try {
            builder.show()
        } catch (_: android.view.WindowManager.BadTokenException) {
            null
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        scopeJob.cancel()
    }
}
