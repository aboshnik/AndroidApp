package com.example.app

import android.os.Bundle
import android.graphics.drawable.GradientDrawable
import android.content.Intent
import android.view.LayoutInflater
import android.view.Gravity
import android.widget.Toast
import android.widget.GridLayout
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import androidx.appcompat.app.AlertDialog
import com.example.app.api.ApiClient
import com.example.app.api.EmployeeWorkSchedule
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.text.SimpleDateFormat
import java.text.DateFormatSymbols
import java.util.Calendar
import java.util.Date
import java.util.Locale

class CalendarActivity : BaseActivity() {
    private var currentLogin: String = ""
    private var currentEmployeeId: String = ""
    private var currentLastName: String = ""
    private var currentFirstName: String = ""
    private var currentPhone: String = ""
    private var currentCanCreatePosts: Boolean = false
    private var currentCanUseDevConsole: Boolean = false
    private var cachedSchedule: EmployeeWorkSchedule? = null
    private lateinit var calendarGrid: GridLayout
    private var shownYear: Int = 0
    private var shownMonth: Int = 0


    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (!SessionManager.requireActive(this)) return
        setContentView(R.layout.activity_calendar)

        val auth = getSharedPreferences("auth", MODE_PRIVATE)
        currentLogin = (intent.getStringExtra("login") ?: auth.getString("login", "")).orEmpty().trim()
        currentEmployeeId = (intent.getStringExtra("employeeId") ?: auth.getString("employeeId", "")).orEmpty()
        currentLastName = (intent.getStringExtra("lastName") ?: auth.getString("lastName", "")).orEmpty()
        currentFirstName = (intent.getStringExtra("firstName") ?: auth.getString("firstName", "")).orEmpty()
        currentPhone = (intent.getStringExtra("phone") ?: auth.getString("phone", "")).orEmpty()
        currentCanCreatePosts = intent.getBooleanExtra("canCreatePosts", auth.getBoolean("canCreatePosts", false))
        currentCanUseDevConsole = intent.getBooleanExtra("canUseDevConsole", auth.getBoolean("canUseDevConsole", false))

        val title = findViewById<TextView>(R.id.tvCalendarTitle)
        calendarGrid = findViewById(R.id.gridCalendar)
        setupBottomNav()

        val calendar = Calendar.getInstance()
        shownYear = calendar.get(Calendar.YEAR)
        shownMonth = calendar.get(Calendar.MONTH) // 0-11

        val ru = Locale("ru")
        val monthNamesNominative = arrayOf(
            "Январь", "Февраль", "Март", "Апрель", "Май", "Июнь",
            "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь"
        )
        title.text = monthNamesNominative.getOrNull(shownMonth) ?: DateFormatSymbols(ru).months[shownMonth]
        renderCalendarGrid()

        loadWorkSchedule()
    }

    private fun setupBottomNav() {
        findViewById<android.view.View>(R.id.navHome).setOnClickListener {
            val i = Intent(this, HomeActivity::class.java)
            i.putExtra("employeeId", currentEmployeeId)
            i.putExtra("login", currentLogin)
            i.putExtra("lastName", currentLastName)
            i.putExtra("firstName", currentFirstName)
            i.putExtra("phone", currentPhone)
            i.putExtra("canCreatePosts", currentCanCreatePosts)
            i.putExtra("canUseDevConsole", currentCanUseDevConsole)
            startActivity(i)
            finish()
        }

        findViewById<android.view.View>(R.id.navCalendar).setOnClickListener { }

        findViewById<android.view.View>(R.id.navProfile).setOnClickListener {
            val i = Intent(this, ProfileActivity::class.java)
            i.putExtra("employeeId", currentEmployeeId)
            i.putExtra("login", currentLogin)
            i.putExtra("lastName", currentLastName)
            i.putExtra("firstName", currentFirstName)
            i.putExtra("phone", currentPhone)
            i.putExtra("canCreatePosts", currentCanCreatePosts)
            i.putExtra("canUseDevConsole", currentCanUseDevConsole)
            startActivity(i)
            finish()
        }
    }

    private fun loadWorkSchedule() {
        scope.launch {
            try {
                val response = withContext(Dispatchers.IO) {
                    ApiClient.employeeApi.getWorkSchedule(
                        employeeId = currentEmployeeId.ifBlank { null },
                        login = currentLogin.ifBlank { null }
                    )
                }
                val body = response.body()
                if (!response.isSuccessful || body == null || !body.success) {
                    cachedSchedule = null
                    renderCalendarGrid()
                    return@launch
                }
                cachedSchedule = body.schedule
                renderCalendarGrid()
            } catch (_: Exception) {
                cachedSchedule = null
                renderCalendarGrid()
            }
        }
    }

    private fun renderCalendarGrid() {
        calendarGrid.removeAllViews()

        val firstDayCal = Calendar.getInstance().apply {
            set(Calendar.YEAR, shownYear)
            set(Calendar.MONTH, shownMonth)
            set(Calendar.DAY_OF_MONTH, 1)
        }
        val daysInMonth = firstDayCal.getActualMaximum(Calendar.DAY_OF_MONTH)

        val firstDayOfWeek = firstDayCal.get(Calendar.DAY_OF_WEEK)
        val offset = ((firstDayOfWeek + 5) % 7)

        val today = Calendar.getInstance()
        val todayYear = today.get(Calendar.YEAR)
        val todayMonth = today.get(Calendar.MONTH)
        val todayDay = today.get(Calendar.DAY_OF_MONTH)

        val totalCells = 42
        for (cell in 0 until totalCells) {
            val tv = TextView(this).apply {
                layoutParams = GridLayout.LayoutParams().apply {
                    val row = cell / 7
                    val col = cell % 7
                    rowSpec = GridLayout.spec(row)
                    width = 0
                    height = GridLayout.LayoutParams.WRAP_CONTENT
                    columnSpec = GridLayout.spec(col, 1f)
                    setMargins(4, 4, 4, 4)
                }
                gravity = Gravity.CENTER
                textSize = 15f
                setTextColor(0xFF212121.toInt())
            }

            val dayNumber = cell - offset + 1
            if (dayNumber in 1..daysInMonth) {
                tv.text = dayNumber.toString()
                val dayCal = Calendar.getInstance().apply {
                    set(Calendar.YEAR, shownYear)
                    set(Calendar.MONTH, shownMonth)
                    set(Calendar.DAY_OF_MONTH, dayNumber)
                    set(Calendar.HOUR_OF_DAY, 0)
                    set(Calendar.MINUTE, 0)
                    set(Calendar.SECOND, 0)
                    set(Calendar.MILLISECOND, 0)
                }
                val dow = dayCal.get(Calendar.DAY_OF_WEEK)

                val weekendColor = 0xFFFFCDD2.toInt()
                val vacationColor = 0xFFBBDEFB.toInt()
                var bgColor: Int? = null

                if (dow == Calendar.SATURDAY || dow == Calendar.SUNDAY) {
                    bgColor = weekendColor
                }
                if (isVacationDate(dayCal.time)) {
                    bgColor = vacationColor
                }

                val isToday = (shownYear == todayYear && shownMonth == todayMonth && dayNumber == todayDay)
                if (isToday) {
                    val density = resources.displayMetrics.density
                    val strokeWidthPx = (2 * density).toInt()
                    val cornerRadiusPx = 6 * density
                    val strokeColor = 0xFF206877.toInt()
                    val drawable = GradientDrawable().apply {
                        shape = GradientDrawable.RECTANGLE
                        cornerRadius = cornerRadiusPx
                        setStroke(strokeWidthPx, strokeColor)
                        setColor(bgColor ?: 0x00000000)
                    }
                    tv.background = drawable
                } else if (bgColor != null) {
                    tv.setBackgroundColor(bgColor)
                } else {
                    tv.background = null
                }

                tv.setOnClickListener {
                    onDayClicked(shownYear, shownMonth, dayNumber)
                }
            } else {
                tv.text = ""
            }

            calendarGrid.addView(tv)
        }
    }

    private fun onDayClicked(year: Int, monthZeroBased: Int, day: Int) {
        val schedule = cachedSchedule
        if (schedule == null) {
            Toast.makeText(this, "Данные графика не заполнены", Toast.LENGTH_SHORT).show()
            return
        }

        val selected = Calendar.getInstance().apply {
            set(Calendar.YEAR, year)
            set(Calendar.MONTH, monthZeroBased)
            set(Calendar.DAY_OF_MONTH, day)
            set(Calendar.HOUR_OF_DAY, 0)
            set(Calendar.MINUTE, 0)
            set(Calendar.SECOND, 0)
            set(Calendar.MILLISECOND, 0)
        }.time

        val vacationStart = parseIsoDate(schedule.vacationStart)
        val vacationEnd = parseIsoDate(schedule.vacationEnd)
        val isVacation = vacationStart != null && vacationEnd != null &&
            !selected.before(vacationStart) && !selected.after(vacationEnd)
        val selectedCalendar = Calendar.getInstance().apply { time = selected }
        val isWeekend = selectedCalendar.get(Calendar.DAY_OF_WEEK) == Calendar.SATURDAY ||
            selectedCalendar.get(Calendar.DAY_OF_WEEK) == Calendar.SUNDAY

        val titleDate = SimpleDateFormat("dd.MM.yyyy", Locale("ru")).format(selected)
        val message = if (isVacation) {
            "У вас отпуск с ${formatDate(vacationStart)} по ${formatDate(vacationEnd)}."
        } else if (isWeekend) {
            "Сегодня выходной день."
        } else {
            val shiftStart = schedule.shiftStart.ifBlank { "не указано" }
            val shiftEnd = schedule.shiftEnd.ifBlank { "не указано" }
            val pattern = schedule.workPattern.ifBlank { "не указан" }
            "Сегодня рабочий день, с $shiftStart до $shiftEnd, график работы $pattern."
        }

        val dialogView = LayoutInflater.from(this).inflate(R.layout.dialog_calendar_day_info, null, false)
        val tvTitle = dialogView.findViewById<TextView>(R.id.tvDialogDateTitle)
        val tvMessage = dialogView.findViewById<TextView>(R.id.tvDialogDateMessage)
        val btnOk = dialogView.findViewById<com.google.android.material.button.MaterialButton>(R.id.btnDialogOk)
        tvTitle.text = "Дата: $titleDate"
        tvMessage.text = message

        if (!canShowUi()) return
        val dialog = AlertDialog.Builder(this)
            .setView(dialogView)
            .create()
        btnOk.setOnClickListener { dialog.dismiss() }
        runCatching { dialog.show() }
    }

    private fun parseIsoDate(raw: String?): Date? {
        val cleaned = raw?.trim().orEmpty()
        if (cleaned.isBlank()) return null
        val onlyDate = if (cleaned.length >= 10) cleaned.substring(0, 10) else cleaned
        return runCatching {
            SimpleDateFormat("yyyy-MM-dd", Locale.US).parse(onlyDate)
        }.getOrNull()
    }

    private fun formatDate(date: Date?): String {
        if (date == null) return "не указано"
        return SimpleDateFormat("dd.MM.yyyy", Locale("ru")).format(date)
    }

    private fun isVacationDate(date: Date): Boolean {
        val schedule = cachedSchedule ?: return false
        val vacationStart = parseIsoDate(schedule.vacationStart) ?: return false
        val vacationEnd = parseIsoDate(schedule.vacationEnd) ?: return false
        return !date.before(vacationStart) && !date.after(vacationEnd)
    }

    override fun onResume() {
        super.onResume()
        SessionManager.touch(this)
    }

}

