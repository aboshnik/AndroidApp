package com.example.app

import android.app.DatePickerDialog
import android.app.TimePickerDialog
import android.content.Intent
import android.os.Bundle
import android.text.InputType
import android.view.Gravity
import android.view.View
import android.widget.CompoundButton
import android.widget.LinearLayout
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import com.example.app.api.PollCreateRequest
import com.google.android.material.textfield.TextInputEditText
import com.google.gson.Gson
import java.text.SimpleDateFormat
import java.util.Calendar
import java.util.Locale
import java.util.TimeZone

class PollEditorActivity : AppCompatActivity() {
    private val gson = Gson()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_poll_editor)

        val optionsContainer = findViewById<LinearLayout>(R.id.pollOptionsContainer)
        ensureDefaultOptions(optionsContainer)

        val existingJson = intent.getStringExtra(EXTRA_POLL_JSON).orEmpty()
        if (existingJson.isNotBlank()) {
            runCatching { gson.fromJson(existingJson, PollCreateRequest::class.java) }.getOrNull()?.let {
                bindExistingPoll(it, optionsContainer)
            }
        }

        findViewById<View>(R.id.btnClosePoll).setOnClickListener { finish() }
        findViewById<View>(R.id.btnAddPollOption).setOnClickListener { addOptionField(optionsContainer, "") }
        findViewById<TextInputEditText>(R.id.etPollEndsAtUtc).setOnClickListener { openDateTimePicker() }
        findViewById<View>(R.id.btnSavePoll).setOnClickListener {
            val poll = collectPollOrShowError(optionsContainer) ?: return@setOnClickListener
            val result = Intent().putExtra(EXTRA_POLL_JSON, gson.toJson(poll))
            setResult(RESULT_OK, result)
            finish()
        }
        findViewById<View>(R.id.btnDeletePoll).setOnClickListener {
            val result = Intent().putExtra(EXTRA_DELETE_POLL, true)
            setResult(RESULT_OK, result)
            finish()
        }
    }

    private fun bindExistingPoll(poll: PollCreateRequest, optionsContainer: LinearLayout) {
        findViewById<TextInputEditText>(R.id.etPollQuestion).setText(poll.question)
        findViewById<TextInputEditText>(R.id.etPollDescription).setText(poll.description.orEmpty())
        findViewById<TextInputEditText>(R.id.etPollEndsAtUtc).setText(poll.endsAtUtc.orEmpty())
        findViewById<CompoundButton>(R.id.cbPollShowVoters).isChecked = poll.showVoters
        findViewById<CompoundButton>(R.id.cbPollAllowRevote).isChecked = poll.allowRevote
        findViewById<CompoundButton>(R.id.cbPollShuffle).isChecked = poll.shuffleOptions
        findViewById<CompoundButton>(R.id.cbPollHideResults).isChecked = poll.hideResultsUntilEnd
        findViewById<CompoundButton>(R.id.cbPollCreatorCanView).isChecked = poll.creatorCanViewWithoutVoting

        optionsContainer.removeAllViews()
        poll.options.forEach { addOptionField(optionsContainer, it) }
        ensureDefaultOptions(optionsContainer)
    }

    private fun ensureDefaultOptions(container: LinearLayout) {
        if (container.childCount >= 2) return
        while (container.childCount < 2) addOptionField(container, "")
    }

    private fun addOptionField(container: LinearLayout, value: String) {
        val et = TextInputEditText(this).apply {
            hint = "Вариант ответа"
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_CAP_SENTENCES
            setText(value)
            gravity = Gravity.CENTER_VERTICAL
            setTextColor(ContextCompat.getColor(this@PollEditorActivity, R.color.poll_editor_text))
            setHintTextColor(ContextCompat.getColor(this@PollEditorActivity, R.color.poll_editor_hint))
            setBackgroundColor(android.graphics.Color.TRANSPARENT)
            setPadding(0, 0, 0, 0)
        }
        val lp = LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MATCH_PARENT,
            (48 * resources.displayMetrics.density).toInt()
        )
        lp.topMargin = 8
        et.layoutParams = lp
        container.addView(et)

        val divider = View(this).apply {
            setBackgroundColor(ContextCompat.getColor(this@PollEditorActivity, R.color.poll_editor_divider))
        }
        val dlp = LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MATCH_PARENT,
            (1 * resources.displayMetrics.density).toInt()
        )
        dlp.topMargin = 8
        divider.layoutParams = dlp
        container.addView(divider)
    }

    private fun collectPollOrShowError(optionsContainer: LinearLayout): PollCreateRequest? {
        val question = findViewById<TextInputEditText>(R.id.etPollQuestion).text?.toString()?.trim().orEmpty()
        if (question.isBlank()) {
            Toast.makeText(this, "Введите название опроса", Toast.LENGTH_SHORT).show()
            return null
        }
        val options = mutableListOf<String>()
        for (i in 0 until optionsContainer.childCount) {
            val et = optionsContainer.getChildAt(i) as? TextInputEditText ?: continue
            val text = et.text?.toString()?.trim().orEmpty()
            if (text.isNotBlank()) options += text
        }
        if (options.size < 2) {
            Toast.makeText(this, "Добавьте минимум 2 варианта ответа", Toast.LENGTH_SHORT).show()
            return null
        }
        val unique = options.map { it.lowercase() }.distinct()
        if (unique.size < 2) {
            Toast.makeText(this, "Варианты ответа должны отличаться (минимум 2 разных)", Toast.LENGTH_LONG).show()
            return null
        }
        val endsAt = findViewById<TextInputEditText>(R.id.etPollEndsAtUtc).text?.toString()?.trim().orEmpty()
        if (endsAt.isNotBlank() && !endsAt.matches(Regex("^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}Z$"))) {
            Toast.makeText(this, "Срок: 2026-04-12T18:00:00Z", Toast.LENGTH_LONG).show()
            return null
        }

        return PollCreateRequest(
            question = question,
            description = findViewById<TextInputEditText>(R.id.etPollDescription).text?.toString()?.trim().orEmpty().ifBlank { null },
            options = options,
            allowMediaInQuestionAndOptions = false,
            showVoters = findViewById<CompoundButton>(R.id.cbPollShowVoters).isChecked,
            allowRevote = findViewById<CompoundButton>(R.id.cbPollAllowRevote).isChecked,
            shuffleOptions = findViewById<CompoundButton>(R.id.cbPollShuffle).isChecked,
            endsAtUtc = endsAt.ifBlank { null },
            hideResultsUntilEnd = findViewById<CompoundButton>(R.id.cbPollHideResults).isChecked,
            creatorCanViewWithoutVoting = findViewById<CompoundButton>(R.id.cbPollCreatorCanView).isChecked
        )
    }

    private fun openDateTimePicker() {
        val calendar = Calendar.getInstance()
        DatePickerDialog(
            this,
            { _, year, month, dayOfMonth ->
                TimePickerDialog(
                    this,
                    { _, hourOfDay, minute ->
                        val local = Calendar.getInstance().apply {
                            set(Calendar.YEAR, year)
                            set(Calendar.MONTH, month)
                            set(Calendar.DAY_OF_MONTH, dayOfMonth)
                            set(Calendar.HOUR_OF_DAY, hourOfDay)
                            set(Calendar.MINUTE, minute)
                            set(Calendar.SECOND, 0)
                            set(Calendar.MILLISECOND, 0)
                        }
                        val utcIso = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'", Locale.US).apply {
                            timeZone = TimeZone.getTimeZone("UTC")
                        }.format(local.time)
                        findViewById<TextInputEditText>(R.id.etPollEndsAtUtc).setText(utcIso)
                    },
                    calendar.get(Calendar.HOUR_OF_DAY),
                    calendar.get(Calendar.MINUTE),
                    true
                ).show()
            },
            calendar.get(Calendar.YEAR),
            calendar.get(Calendar.MONTH),
            calendar.get(Calendar.DAY_OF_MONTH)
        ).show()
    }

    companion object {
        const val EXTRA_POLL_JSON = "poll_json"
        const val EXTRA_DELETE_POLL = "delete_poll"
    }
}
