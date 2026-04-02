package com.example.app

import android.os.Bundle
import android.view.View
import android.widget.ProgressBar
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.example.app.api.ApiClient
import com.example.app.api.MarkReadRequest
import com.example.app.api.SecurityDecisionRequest
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class NotificationsActivity : AppCompatActivity() {

    private val scope = CoroutineScope(Dispatchers.Main + Job())
    private lateinit var recycler: RecyclerView
    private lateinit var progress: ProgressBar
    private lateinit var empty: TextView
    private val adapter = NotificationsAdapter(
        items = mutableListOf(),
        onApprove = { attemptId -> decide(true, attemptId) },
        onDeny = { attemptId -> decide(false, attemptId) }
    )

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (!SessionManager.requireActive(this)) return
        setContentView(R.layout.activity_notifications)

        val login = intent.getStringExtra("login").orEmpty()
        if (login.isBlank()) {
            Toast.makeText(this, getString(R.string.error_network), Toast.LENGTH_SHORT).show()
            finish()
            return
        }

        recycler = findViewById(R.id.recyclerNotifications)
        progress = findViewById(R.id.progressNotifications)
        empty = findViewById(R.id.tvNotificationsEmpty)

        recycler.layoutManager = LinearLayoutManager(this)
        recycler.adapter = adapter

        load(login)
    }

    private fun load(login: String) {
        progress.visibility = View.VISIBLE
        empty.visibility = View.GONE

        scope.launch {
            try {
                val response = withContext(Dispatchers.IO) {
                    ApiClient.notificationsApi.getNotifications(login = login)
                }
                progress.visibility = View.GONE

                val body = response.body()
                if (!response.isSuccessful || body == null || !body.success) {
                    Toast.makeText(this@NotificationsActivity, body?.message ?: getString(R.string.error_network), Toast.LENGTH_LONG).show()
                    empty.visibility = View.VISIBLE
                    return@launch
                }

                val items = body.items ?: emptyList()
                adapter.replaceAll(items)
                empty.visibility = if (items.isEmpty()) View.VISIBLE else View.GONE

                // mark all read in background (so badge goes away)
                withContext(Dispatchers.IO) {
                    ApiClient.notificationsApi.markRead(MarkReadRequest(login))
                }
            } catch (e: Exception) {
                progress.visibility = View.GONE
                Toast.makeText(this@NotificationsActivity, "${getString(R.string.error_network)} ${e.message}", Toast.LENGTH_LONG).show()
                empty.visibility = View.VISIBLE
            }
        }
    }

    private fun decide(approve: Boolean, attemptId: Int) {
        val login = intent.getStringExtra("login").orEmpty()
        if (login.isBlank()) return

        scope.launch {
            try {
                val request = SecurityDecisionRequest(login = login, attemptId = attemptId)
                val response = withContext(Dispatchers.IO) {
                    if (approve) ApiClient.securityApi.approve(request) else ApiClient.securityApi.deny(request)
                }
                val body = response.body()
                if (!response.isSuccessful || body == null || !body.success) {
                    Toast.makeText(
                        this@NotificationsActivity,
                        body?.message ?: getString(R.string.error_network),
                        Toast.LENGTH_LONG
                    ).show()
                    return@launch
                }
                Toast.makeText(this@NotificationsActivity, body.message, Toast.LENGTH_SHORT).show()
                // Reload
                load(login)
            } catch (e: Exception) {
                Toast.makeText(
                    this@NotificationsActivity,
                    "${getString(R.string.error_network)} ${e.message}",
                    Toast.LENGTH_LONG
                ).show()
            }
        }
    }

    override fun onResume() {
        super.onResume()
        SessionManager.touch(this)
    }
}

