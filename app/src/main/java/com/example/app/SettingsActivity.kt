package com.example.app

import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.text.InputType
import android.widget.EditText
import android.widget.ImageView
import android.widget.RadioButton
import android.widget.Switch
import android.widget.TextView
import androidx.appcompat.app.AlertDialog
import com.example.app.api.ApiClient
import com.example.app.api.RuntimeApiConfig

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
        val tvApiServerValue = findViewById<android.widget.TextView>(R.id.tvApiServerValue)

        swNotifications.isChecked = UserSettings.areNotificationsEnabled(this)
        val mode = UserSettings.currentThemeMode(this)
        rbLight.isChecked = mode == "light"
        rbDark.isChecked = mode == "dark"

        findViewById<android.view.View>(R.id.rowNotificationsSystem).setOnClickListener {
            openSystemNotificationSettings()
        }
        findViewById<android.view.View>(R.id.rowPersonalData).setOnClickListener {
            startActivity(Intent(this, ProfileActivity::class.java))
        }
        findViewById<android.view.View>(R.id.rowHelp).setOnClickListener {
            safeToast("Помощь и поддержка: скоро")
        }
        findViewById<android.view.View>(R.id.rowLogout).setOnClickListener {
            confirmLogout()
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

        val refreshApiServerLabel = {
            tvApiServerValue.text = ApiClient.currentBaseUrl()
        }
        refreshApiServerLabel()

        findViewById<android.view.View>(R.id.rowApiServer).setOnClickListener {
            val input = EditText(this).apply {
                setText(ApiClient.currentBaseUrl())
                setSelection(text.length)
                hint = "https://api.company.ru/"
                inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_URI
            }
            AlertDialog.Builder(this)
                .setTitle("Адрес API сервера")
                .setMessage("Укажите полный URL (http/https). Например: http://192.168.0.106:5000/")
                .setView(input)
                .setPositiveButton("Сохранить") { _, _ ->
                    val raw = input.text?.toString().orEmpty()
                    try {
                        RuntimeApiConfig.saveBaseUrl(this, raw)
                        ApiClient.refreshIfNeeded()
                        refreshApiServerLabel()
                        safeToast("Адрес сервера обновлен")
                    } catch (_: IllegalArgumentException) {
                        safeToast("Введите корректный URL", long = true)
                    }
                }
                .setNeutralButton("Сбросить") { _, _ ->
                    RuntimeApiConfig.clearBaseUrl(this)
                    ApiClient.refreshIfNeeded()
                    refreshApiServerLabel()
                    safeToast("Адрес сервера сброшен")
                }
                .setNegativeButton("Отмена", null)
                .show()
        }

        findViewById<android.view.View>(R.id.navHome).setOnClickListener {
            startActivity(Intent(this, HomeActivity::class.java))
        }
        findViewById<android.view.View>(R.id.navChats).setOnClickListener {
            setBottomTab("chats")
            startActivity(Intent(this, com.example.app.chats.ChatsActivity::class.java))
        }
        findViewById<android.view.View>(R.id.navSettings).setOnClickListener { }
        findViewById<android.view.View>(R.id.navProfile).setOnClickListener {
            startActivity(Intent(this, ProfileActivity::class.java))
        }
        findViewById<android.view.View>(R.id.navContacts).setOnClickListener {
            startActivity(Intent(this, ShopActivity::class.java))
        }
        setBottomTab("settings")
    }

    override fun onResume() {
        super.onResume()
        setBottomTab("settings")
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

    private fun showContactsDialog() {
        val view = layoutInflater.inflate(R.layout.dialog_contacts, null, false)
        safeShowDialog(
            AlertDialog.Builder(this)
                .setView(view)
                .setPositiveButton("Закрыть", null)
        )
    }

    private fun setBottomTab(tab: String) {
        val active = getColor(R.color.nav_active)
        val inactive = getColor(R.color.nav_inactive)
        fun setItem(containerId: Int, iconId: Int, textId: Int, activeTab: Boolean) {
            val container = findViewById<android.view.View>(containerId)
            findViewById<ImageView>(iconId).setColorFilter(if (activeTab) active else inactive)
            val tv = findViewById<TextView>(textId)
            tv.setTextColor(if (activeTab) active else inactive)
            tv.setTypeface(null, if (activeTab) android.graphics.Typeface.BOLD else android.graphics.Typeface.NORMAL)
            container.setBackgroundResource(
                if (activeTab) R.drawable.bg_bottom_nav_item_active
                else android.R.color.transparent
            )
        }
        setItem(R.id.navHome, R.id.navHomeIcon, R.id.navHomeText, tab == "home")
        setItem(R.id.navChats, R.id.navChatsIcon, R.id.navChatsText, tab == "chats")
        setItem(R.id.navSettings, R.id.navSettingsIcon, R.id.navSettingsText, tab == "settings")
        setItem(R.id.navProfile, R.id.navProfileIcon, R.id.navProfileText, tab == "profile")
        setItem(R.id.navContacts, R.id.navContactsIcon, R.id.navContactsText, tab == "store")
    }

    private fun confirmLogout() {
        safeShowDialog(
            AlertDialog.Builder(this)
                .setTitle(getString(R.string.logout_title))
                .setMessage(getString(R.string.logout_message))
                .setPositiveButton(getString(R.string.logout_button)) { _, _ ->
                    SessionManager.clear(this)
                    val i = Intent(this, MainActivity::class.java).apply {
                        addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK)
                    }
                    startActivity(i)
                    finish()
                }
                .setNegativeButton(android.R.string.cancel, null)
        )
    }
}
