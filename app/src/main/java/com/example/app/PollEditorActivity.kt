package com.example.app

import android.os.Bundle
import android.app.DatePickerDialog
import android.app.TimePickerDialog
import android.view.View
import android.widget.CheckBox
import android.widget.EditText
import androidx.recyclerview.widget.ItemTouchHelper
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.example.app.api.PollCreateRequest
import java.time.LocalDateTime
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.util.Locale

class PollEditorActivity : BaseActivity() {
    companion object {
        const val EXTRA_POLL_JSON = "poll_json"
    }

    private lateinit var etQuestion: EditText
    private lateinit var etDescription: EditText
    private lateinit var optionsContainer: RecyclerView
    private lateinit var cbShowVoters: CheckBox
    private lateinit var cbAllowRevote: CheckBox
    private lateinit var cbShuffle: CheckBox
    private lateinit var cbHideResults: CheckBox
    private lateinit var cbCreatorCanView: CheckBox
    private lateinit var etEndsAtUtc: EditText
    private lateinit var btnClearEndsAt: View
    private var selectedEndsAtUtc: String? = null
    private lateinit var optionsAdapter: PollOptionsAdapter

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_poll_editor)

        etQuestion = findViewById(R.id.etPollQuestion)
        etDescription = findViewById(R.id.etPollDescription)
        optionsContainer = findViewById(R.id.pollOptionsContainer)
        cbShowVoters = findViewById(R.id.cbPollShowVoters)
        cbAllowRevote = findViewById(R.id.cbPollAllowRevote)
        cbShuffle = findViewById(R.id.cbPollShuffle)
        cbHideResults = findViewById(R.id.cbPollHideResults)
        cbCreatorCanView = findViewById(R.id.cbPollCreatorCanView)
        etEndsAtUtc = findViewById(R.id.etPollEndsAtUtc)
        btnClearEndsAt = findViewById(R.id.btnClearPollEndsAtUtc)

        findViewById<View>(R.id.btnClosePoll).setOnClickListener { finish() }
        findViewById<View>(R.id.btnDeletePoll).setOnClickListener { clearAndClose() }
        findViewById<View>(R.id.btnAddPollOption).setOnClickListener { optionsAdapter.addOption("") }
        findViewById<View>(R.id.btnSavePoll).setOnClickListener { savePoll() }
        etEndsAtUtc.setOnClickListener { pickEndsAtDateTime() }
        btnClearEndsAt.setOnClickListener {
            selectedEndsAtUtc = null
            etEndsAtUtc.setText("")
        }

        bindRowToggle(cbShowVoters)
        bindRowToggle(cbAllowRevote)
        bindRowToggle(cbShuffle)
        bindRowToggle(cbHideResults)
        bindRowToggle(cbCreatorCanView)

        optionsAdapter = PollOptionsAdapter(mutableListOf()) { vh ->
            itemTouchHelper.startDrag(vh)
        }
        optionsContainer.layoutManager = LinearLayoutManager(this)
        optionsContainer.adapter = optionsAdapter

        val initialJson = intent.getStringExtra(EXTRA_POLL_JSON).orEmpty()
        if (initialJson.isNotBlank()) {
            loadDraft(initialJson)
        } else {
            optionsAdapter.addOption("")
            optionsAdapter.addOption("")
        }
    }

    private val itemTouchHelper by lazy {
        ItemTouchHelper(object : ItemTouchHelper.SimpleCallback(
            ItemTouchHelper.UP or ItemTouchHelper.DOWN,
            0
        ) {
            override fun onMove(
                recyclerView: RecyclerView,
                viewHolder: RecyclerView.ViewHolder,
                target: RecyclerView.ViewHolder
            ): Boolean {
                optionsAdapter.move(viewHolder.bindingAdapterPosition, target.bindingAdapterPosition)
                return true
            }

            override fun onSwiped(viewHolder: RecyclerView.ViewHolder, direction: Int) {}
            override fun isLongPressDragEnabled(): Boolean = false
        }).also { it.attachToRecyclerView(optionsContainer) }
    }

    private fun bindRowToggle(checkBox: CheckBox) {
        val row = checkBox.parent
        if (row is View) {
            row.setOnClickListener { checkBox.isChecked = !checkBox.isChecked }
        }
    }

    private fun clearAndClose() {
        etQuestion.setText("")
        etDescription.setText("")
        optionsAdapter.clearAll()
        selectedEndsAtUtc = null
        etEndsAtUtc.setText("")
        setResult(RESULT_CANCELED)
        finish()
    }

    private fun pickEndsAtDateTime() {
        val now = LocalDateTime.now()
        DatePickerDialog(
            this,
            { _, year, month, dayOfMonth ->
                TimePickerDialog(
                    this,
                    { _, hourOfDay, minute ->
                        val local = LocalDateTime.of(year, month + 1, dayOfMonth, hourOfDay, minute)
                        if (local.isBefore(LocalDateTime.now().plusMinutes(1))) {
                            safeToast("Срок должен быть в будущем")
                            return@TimePickerDialog
                        }
                        val zoned = local.atZone(ZoneId.systemDefault())
                        val utc = zoned.withZoneSameInstant(ZoneOffset.UTC).toOffsetDateTime()
                        selectedEndsAtUtc = utc.format(DateTimeFormatter.ISO_OFFSET_DATE_TIME)
                        etEndsAtUtc.setText(local.format(DateTimeFormatter.ofPattern("dd.MM.yyyy HH:mm", Locale("ru"))))
                    },
                    now.hour,
                    now.minute,
                    true
                ).show()
            },
            now.year,
            now.monthValue - 1,
            now.dayOfMonth
        ).show()
    }

    private fun loadDraft(json: String) {
        val poll = runCatching { com.google.gson.Gson().fromJson(json, PollCreateRequest::class.java) }.getOrNull()
        if (poll == null) {
            optionsAdapter.replaceAll(listOf("", ""))
            return
        }
        etQuestion.setText(poll.question)
        etDescription.setText(poll.description.orEmpty())
        cbShowVoters.isChecked = poll.showVoters
        cbAllowRevote.isChecked = poll.allowRevote
        cbShuffle.isChecked = poll.shuffleOptions
        cbHideResults.isChecked = poll.hideResultsUntilEnd
        cbCreatorCanView.isChecked = poll.creatorCanViewWithoutVoting
        val prefill = poll.options
        val data = if (prefill.isEmpty()) listOf("", "") else prefill
        optionsAdapter.replaceAll(data)
        selectedEndsAtUtc = poll.endsAtUtc
        etEndsAtUtc.setText(formatEndsAtForDisplay(poll.endsAtUtc))
    }

    private fun formatEndsAtForDisplay(utc: String?): String {
        if (utc.isNullOrBlank()) return ""
        return runCatching {
            val odt = OffsetDateTime.parse(utc)
            odt.atZoneSameInstant(ZoneId.systemDefault())
                .toLocalDateTime()
                .format(DateTimeFormatter.ofPattern("dd.MM.yyyy HH:mm", Locale("ru")))
        }.getOrElse { utc }
    }

    private fun savePoll() {
        val q = etQuestion.text?.toString()?.trim().orEmpty()
        if (q.isBlank()) {
            safeToast("Введите вопрос")
            return
        }
        val options = optionsAdapter.getOptions().map { it.trim() }.filter { it.isNotBlank() }
        if (options.size < 2) {
            safeToast("Добавьте минимум 2 варианта")
            return
        }

        val poll = PollCreateRequest(
            question = q,
            description = etDescription.text?.toString()?.trim().orEmpty().ifBlank { null },
            options = options,
            showVoters = cbShowVoters.isChecked,
            allowRevote = cbAllowRevote.isChecked,
            shuffleOptions = cbShuffle.isChecked,
            endsAtUtc = selectedEndsAtUtc,
            hideResultsUntilEnd = cbHideResults.isChecked,
            creatorCanViewWithoutVoting = cbCreatorCanView.isChecked
        )

        val json = com.google.gson.Gson().toJson(poll)
        setResult(RESULT_OK, android.content.Intent().putExtra(EXTRA_POLL_JSON, json))
        finish()
    }
}

