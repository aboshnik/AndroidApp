package com.example.app

import android.content.Intent
import android.os.Bundle
import android.widget.GridLayout
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
            startActivity(Intent(this, com.example.app.chats.ChatsActivity::class.java))
        }
        findViewById<android.view.View>(R.id.navSettings).setOnClickListener {
            startActivity(Intent(this, SettingsActivity::class.java))
        }
        findViewById<android.view.View>(R.id.navProfile).setOnClickListener {
            startActivity(Intent(this, ProfileActivity::class.java))
        }

        renderMonth()
        loadSchedule()
    }

    override fun onResume() {
        super.onResume()
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
                minHeight = 42.dp()
                gravity = android.view.Gravity.CENTER
                textSize = 14f
                setBackgroundResource(R.drawable.bg_profile_action_row)
            }

            val dayNum = index - startOffset + 1
            if (dayNum in 1..daysInMonth) {
                val date = currentMonth.atDay(dayNum)
                cell.text = dayNum.toString()
                val weekend = date.dayOfWeek == DayOfWeek.SATURDAY || date.dayOfWeek == DayOfWeek.SUNDAY
                val isVacation = vacationRange?.let { !date.isBefore(it.first) && !date.isAfter(it.second) } == true
                if (isVacation) {
                    cell.setBackgroundColor(0xFFBBDEFB.toInt())
                } else if (weekend) {
                    cell.setBackgroundColor(0xFFFFCDD2.toInt())
                }
                if (date == today) {
                    cell.setBackgroundResource(R.drawable.bg_profile_action_row)
                    cell.setTextColor(getColor(R.color.button_primary))
                } else {
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
        val btn = view.findViewById<com.google.android.material.button.MaterialButton>(R.id.btnDialogOk)

        title.text = date.format(DateTimeFormatter.ofPattern("dd MMMM yyyy", Locale("ru")))
        val weekend = date.dayOfWeek == DayOfWeek.SATURDAY || date.dayOfWeek == DayOfWeek.SUNDAY
        val vacationRange = getVacationRange()
        val isVacation = vacationRange?.let { !date.isBefore(it.first) && !date.isAfter(it.second) } == true
        msg.text = when {
            isVacation -> "Отпуск"
            weekend -> "Выходной день"
            else -> "Рабочий день\n${buildShiftText()}"
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
}
