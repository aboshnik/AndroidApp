package com.example.app

import android.content.Intent
import android.os.Bundle
import android.view.View
import android.widget.GridLayout
import android.widget.ImageView
import android.widget.TextView
import androidx.appcompat.app.AlertDialog
import com.example.app.api.ApiClient
import com.example.app.api.EmployeeWorkSchedule
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.time.DayOfWeek
import java.time.LocalDate
import java.time.YearMonth
import java.time.format.DateTimeFormatter
import java.util.Locale

class CalendarActivity : BaseActivity() {
    private lateinit var tvTitle: TextView
    private lateinit var grid: GridLayout
    private var currentMonth: YearMonth = YearMonth.now()
    private var employeeId: String = ""
    private var login: String = ""
    private var schedule: EmployeeWorkSchedule? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (!SessionManager.requireActive(this)) return
        setContentView(R.layout.activity_calendar)
        val auth = getSharedPreferences("auth", MODE_PRIVATE)
        employeeId = auth.getString("employeeId", "")?.trim().orEmpty()
        login = auth.getString("login", "")?.trim().orEmpty()

        tvTitle = findViewById(R.id.tvCalendarTitle)
        grid = findViewById(R.id.gridCalendar)

        findViewById<TextView>(R.id.btnPrevMonth).setOnClickListener {
            currentMonth = currentMonth.minusMonths(1)
            renderMonth()
        }
        findViewById<TextView>(R.id.btnNextMonth).setOnClickListener {
            currentMonth = currentMonth.plusMonths(1)
            renderMonth()
        }
        findViewById<TextView>(R.id.btnToday).setOnClickListener {
            currentMonth = YearMonth.now()
            renderMonth()
        }

        findViewById<android.view.View>(R.id.navHome).setOnClickListener {
            startActivity(Intent(this, HomeActivity::class.java))
        }
        findViewById<android.view.View>(R.id.navChats).setOnClickListener {
            setBottomTab("chats")
            startActivity(Intent(this, com.example.app.chats.ChatsActivity::class.java))
        }
        findViewById<android.view.View>(R.id.navSettings).setOnClickListener {
            startActivity(Intent(this, SettingsActivity::class.java))
        }
        findViewById<android.view.View>(R.id.navProfile).setOnClickListener {
            startActivity(Intent(this, ProfileActivity::class.java))
        }

        setBottomTab("calendar")
        renderMonth()
        loadSchedule()
    }

    override fun onResume() {
        super.onResume()
        setBottomTab("calendar")
        startChatsUnreadBadgeAutoRefresh(employeeId = employeeId, badgeViewId = R.id.navChatsBadge)
    }

    override fun onPause() {
        super.onPause()
        stopChatsUnreadBadgeAutoRefresh()
    }

    private fun loadSchedule() {
        scope.launch {
            try {
                val resp = withContext(Dispatchers.IO) {
                    ApiClient.employeeApi.getWorkSchedule(
                        employeeId = employeeId.ifBlank { null },
                        login = login.ifBlank { null }
                    )
                }
                val body = resp.body()
                schedule = if (resp.isSuccessful && body?.success == true) body.schedule else null
                renderMonth()
            } catch (_: Exception) {
                schedule = null
            }
        }
    }

    private fun renderMonth() {
        tvTitle.text = currentMonth.format(DateTimeFormatter.ofPattern("LLLL yyyy", Locale("ru")))
            .replaceFirstChar { if (it.isLowerCase()) it.titlecase(Locale("ru")) else it.toString() }
        grid.removeAllViews()

        val first = currentMonth.atDay(1)
        val startOffset = ((first.dayOfWeek.value + 6) % 7) // Monday=0
        val daysInMonth = currentMonth.lengthOfMonth()
        val today = LocalDate.now()
        val vacationRange = getVacationRange()

        repeat(42) { index ->
            val cell = TextView(this).apply {
                layoutParams = GridLayout.LayoutParams().apply {
                    width = 0
                    columnSpec = GridLayout.spec(GridLayout.UNDEFINED, 1f)
                    setMargins(4.dp(), 4.dp(), 4.dp(), 4.dp())
                }
                minHeight = 44.dp()
                gravity = android.view.Gravity.CENTER
                textSize = 14f
                setBackgroundResource(R.drawable.bg_calendar_day_default)
            }

            val dayNum = index - startOffset + 1
            if (dayNum in 1..daysInMonth) {
                val date = currentMonth.atDay(dayNum)
                cell.text = dayNum.toString()
                val weekend = date.dayOfWeek == DayOfWeek.SATURDAY || date.dayOfWeek == DayOfWeek.SUNDAY
                val isVacation = vacationRange?.let { !date.isBefore(it.first) && !date.isAfter(it.second) } == true
                if (date == today) {
                    cell.setBackgroundResource(R.drawable.bg_calendar_day_today)
                    cell.setTextColor(getColor(android.R.color.white))
                } else if (isVacation) {
                    cell.setBackgroundResource(R.drawable.bg_calendar_day_vacation)
                    cell.setTextColor(getColor(R.color.text_primary))
                } else if (weekend) {
                    cell.setBackgroundResource(R.drawable.bg_calendar_day_weekend)
                    cell.setTextColor(0xFFBE185D.toInt())
                } else {
                    cell.setBackgroundResource(R.drawable.bg_calendar_day_default)
                    cell.setTextColor(getColor(R.color.text_primary))
                }
                cell.setOnClickListener { showDayDialog(date) }
            } else {
                cell.text = ""
                cell.alpha = 0.35f
            }
            grid.addView(cell)
        }
    }

    private fun showDayDialog(date: LocalDate) {
        val view = layoutInflater.inflate(R.layout.dialog_calendar_day, null, false)
        val title = view.findViewById<TextView>(R.id.tvDialogDateTitle)
        val msg = view.findViewById<TextView>(R.id.tvDialogDateMessage)
        val cardWork = view.findViewById<View>(R.id.cardWork)
        val cardSchedule = view.findViewById<View>(R.id.cardSchedule)
        val cardWeekend = view.findViewById<View>(R.id.cardWeekend)
        val cardVacation = view.findViewById<View>(R.id.cardVacation)
        val tvWorkSubtitle = view.findViewById<TextView>(R.id.tvWorkSubtitle)
        val tvScheduleSubtitle = view.findViewById<TextView>(R.id.tvScheduleSubtitle)
        val btn = view.findViewById<com.google.android.material.button.MaterialButton>(R.id.btnDialogOk)

        title.text = date.format(DateTimeFormatter.ofPattern("d MMMM yyyy", Locale("ru")))
        val weekend = date.dayOfWeek == DayOfWeek.SATURDAY || date.dayOfWeek == DayOfWeek.SUNDAY
        val vacationRange = getVacationRange()
        val isVacation = vacationRange?.let { !date.isBefore(it.first) && !date.isAfter(it.second) } == true
        val shiftText = buildShiftText()
        val hasSchedule = schedule != null

        cardWork.visibility = View.GONE
        cardSchedule.visibility = View.GONE
        cardWeekend.visibility = View.GONE
        cardVacation.visibility = View.GONE

        msg.text = when {
            isVacation -> "Отпуск"
            weekend -> "Выходной день"
            else -> "Рабочий день"
        }
        when {
            isVacation -> {
                cardVacation.visibility = View.VISIBLE
            }
            weekend -> {
                cardWeekend.visibility = View.VISIBLE
            }
            else -> {
                cardWork.visibility = View.VISIBLE
                tvWorkSubtitle.text = "График: ${schedule?.workPattern?.ifBlank { "-" } ?: "-"}"
                if (hasSchedule) {
                    cardSchedule.visibility = View.VISIBLE
                    tvScheduleSubtitle.text = shiftText.removePrefix("График: ")
                }
            }
        }

        val dialog = safeShowDialog(AlertDialog.Builder(this).setView(view))
        btn.setOnClickListener { dialog?.dismiss() }
    }

    private fun buildShiftText(): String {
        val s = schedule ?: return "График: нет данных"
        val pattern = s.workPattern.ifBlank { "-" }
        val from = s.shiftStart.ifBlank { "--:--" }
        val to = s.shiftEnd.ifBlank { "--:--" }
        return "График: $pattern, смена $from - $to"
    }

    private fun getVacationRange(): Pair<LocalDate, LocalDate>? {
        val s = schedule ?: return null
        val from = parseIsoDate(s.vacationStart) ?: return null
        val to = parseIsoDate(s.vacationEnd) ?: return null
        if (to.isBefore(from)) return null
        return from to to
    }

    private fun parseIsoDate(raw: String?): LocalDate? {
        val value = raw?.trim().orEmpty()
        if (value.isBlank()) return null
        return runCatching { LocalDate.parse(value) }.getOrNull()
    }

    private fun Int.dp(): Int = (this * resources.displayMetrics.density).toInt()

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
            val container = findViewById<View>(containerId)
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
    }
}
