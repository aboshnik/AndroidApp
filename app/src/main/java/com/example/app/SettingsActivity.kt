package com.example.app

import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.widget.RadioButton
import android.widget.Switch

class SettingsActivity : BaseActivity() {
    override fun swipeTabIndex(): Int = 2
    private var employeeId: String = ""

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (!SessionManager.requireActive(this)) return
        setContentView(R.layout.activity_settings)
        val auth = getSharedPreferences("auth", MODE_PRIVATE)
        employeeId = auth.getString("employeeId", "")?.trim().orEmpty()

        val swNotifications = findViewById<Switch>(R.id.swNotifications)
        val rbLight = findViewById<RadioButton>(R.id.rbThemeLight)
        val rbDark = findViewById<RadioButton>(R.id.rbThemeDark)

        swNotifications.isChecked = UserSettings.areNotificationsEnabled(this)
        val mode = UserSettings.currentThemeMode(this)
        rbLight.isChecked = mode == "light"
        rbDark.isChecked = mode == "dark"

        findViewById<android.view.View>(R.id.rowNotificationsSystem).setOnClickListener {
            openSystemNotificationSettings()
        }
        swNotifications.setOnCheckedChangeListener { _, checked ->
            UserSettings.setNotificationsEnabled(this, checked)
        }

        rbLight.setOnClickListener {
            UserSettings.setThemeMode(this, "light")
        }
        rbDark.setOnClickListener {
            UserSettings.setThemeMode(this, "dark")
        }

        findViewById<android.view.View>(R.id.navHome).setOnClickListener {
            startActivity(Intent(this, HomeActivity::class.java))
        }
        findViewById<android.view.View>(R.id.navChats).setOnClickListener {
            startActivity(Intent(this, com.example.app.chats.ChatsActivity::class.java))
        }
        findViewById<android.view.View>(R.id.navSettings).setOnClickListener { }
        findViewById<android.view.View>(R.id.navProfile).setOnClickListener {
            startActivity(Intent(this, ProfileActivity::class.java))
        }
    }

    override fun onResume() {
        super.onResume()
        startChatsUnreadBadgeAutoRefresh(employeeId = employeeId, badgeViewId = R.id.navChatsBadge)
    }

    override fun onPause() {
        super.onPause()
        stopChatsUnreadBadgeAutoRefresh()
    }

    private fun openSystemNotificationSettings() {
        val intent = if (Build.VERSION.SDK_INT >= 26) {
            Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS).apply {
                putExtra(Settings.EXTRA_APP_PACKAGE, packageName)
            }
        } else {
            Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS).apply {
                data = Uri.fromParts("package", packageName, null)
            }
        }
        runCatching { startActivity(intent) }
    }
}
