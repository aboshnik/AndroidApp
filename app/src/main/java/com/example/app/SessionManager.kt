package com.example.app

import android.content.Context
import android.content.Intent
import android.os.Handler
import android.os.Looper
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import java.lang.ref.WeakReference

object SessionManager {
    private const val PREFS = "auth"
    private const val KEY_LOGIN = "login"
    private const val KEY_EMPLOYEE_ID = "employeeId"
    private const val KEY_LAST_NAME = "lastName"
    private const val KEY_FIRST_NAME = "firstName"
    private const val KEY_PHONE = "phone"
    private const val KEY_CAN_CREATE_POSTS = "canCreatePosts"
    private const val KEY_CAN_USE_DEV_CONSOLE = "canUseDevConsole"
    private const val KEY_LAST_ACTIVE = "last_active_ms"

    private const val TIMEOUT_MS = 5 * 60 * 1000L

    private val mainHandler = Handler(Looper.getMainLooper())
    private var logoutTask: Runnable? = null

    fun isActive(context: Context): Boolean {
        val p = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        val login = p.getString(KEY_LOGIN, "")?.trim().orEmpty()
        if (login.isBlank()) return false
        val last = p.getLong(KEY_LAST_ACTIVE, 0L)
        if (last == 0L) return false
        return (System.currentTimeMillis() - last) <= TIMEOUT_MS
    }

    fun touch(context: Context) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            .edit()
            .putLong(KEY_LAST_ACTIVE, System.currentTimeMillis())
            .apply()
    }

    fun saveFromLogin(
        context: Context,
        login: String,
        employeeId: String,
        lastName: String,
        firstName: String,
        phone: String,
        canCreatePosts: Boolean,
        canUseDevConsole: Boolean
    ) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit()
            .putString(KEY_LOGIN, login)
            .putString(KEY_EMPLOYEE_ID, employeeId)
            .putString(KEY_LAST_NAME, lastName)
            .putString(KEY_FIRST_NAME, firstName)
            .putString(KEY_PHONE, phone)
            .putBoolean(KEY_CAN_CREATE_POSTS, canCreatePosts)
            .putBoolean(KEY_CAN_USE_DEV_CONSOLE, canUseDevConsole)
            .putLong(KEY_LAST_ACTIVE, System.currentTimeMillis())
            .apply()
    }

    fun clear(context: Context) {
        logoutTask?.let { mainHandler.removeCallbacks(it) }
        logoutTask = null

        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit()
            .remove(KEY_LOGIN)
            .remove(KEY_EMPLOYEE_ID)
            .remove(KEY_LAST_NAME)
            .remove(KEY_FIRST_NAME)
            .remove(KEY_PHONE)
            .remove(KEY_CAN_CREATE_POSTS)
            .remove(KEY_CAN_USE_DEV_CONSOLE)
            .remove(KEY_LAST_ACTIVE)
            .apply()
    }

    fun startHomeWithSavedSession(context: Context) {
        val p = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        val i = Intent(context, HomeActivity::class.java).apply {
            putExtra("login", p.getString(KEY_LOGIN, "") ?: "")
            putExtra("employeeId", p.getString(KEY_EMPLOYEE_ID, "") ?: "")
            putExtra("lastName", p.getString(KEY_LAST_NAME, "") ?: "")
            putExtra("firstName", p.getString(KEY_FIRST_NAME, "") ?: "")
            putExtra("phone", p.getString(KEY_PHONE, "") ?: "")
            putExtra("canCreatePosts", p.getBoolean(KEY_CAN_CREATE_POSTS, false))
            putExtra("canUseDevConsole", p.getBoolean(KEY_CAN_USE_DEV_CONSOLE, false))
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK)
        }
        context.startActivity(i)
    }

    fun requireActive(activity: androidx.appcompat.app.AppCompatActivity): Boolean {
        if (isActive(activity)) {
            touch(activity)
            scheduleAutoLogout(activity)
            return true
        }
        clear(activity)
        Toast.makeText(activity, activity.getString(R.string.session_expired), Toast.LENGTH_SHORT).show()
        val i = Intent(activity, MainActivity::class.java).apply {
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK)
        }
        activity.startActivity(i)
        activity.finish()
        return false
    }

    private fun scheduleAutoLogout(activity: AppCompatActivity) {
        // Cancel previous scheduled logout (if any).
        logoutTask?.let { mainHandler.removeCallbacks(it) }

        // After touch() in requireActive(), last_active_ms is "now", so we can just post TIMEOUT_MS.
        val actRef = WeakReference(activity)
        logoutTask = Runnable {
            val act = actRef.get() ?: return@Runnable
            if (act.isFinishing || act.isDestroyed) return@Runnable

            // If session still active - do nothing (it might have been refreshed).
            if (isActive(act)) return@Runnable

            clear(act)
            Toast.makeText(act, act.getString(R.string.session_expired), Toast.LENGTH_SHORT).show()

            val i = Intent(act, MainActivity::class.java).apply {
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK)
            }
            act.startActivity(i)
            act.finish()
        }

        mainHandler.postDelayed(logoutTask!!, TIMEOUT_MS)
    }
}

