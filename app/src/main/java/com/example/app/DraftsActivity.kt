package com.example.app

import android.content.Intent
import android.os.Bundle
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView

class DraftsActivity : AppCompatActivity() {

    private lateinit var draftsAdapter: DraftsAdapter
    private lateinit var emptyView: TextView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (!SessionManager.requireActive(this)) return
        setContentView(R.layout.activity_drafts)

        val recycler = findViewById<RecyclerView>(R.id.recyclerDrafts)
        emptyView = findViewById(R.id.tvDraftsEmpty)

        draftsAdapter = DraftsAdapter(
            items = mutableListOf(),
            onClick = { d ->
                val data = Intent()
                data.putExtra("draftId", d.id)
                data.putExtra("draftContent", d.content)
                data.putStringArrayListExtra("draftUris", ArrayList(d.attachmentUris))
                setResult(RESULT_OK, data)
                finish()
            },
            onLongClick = { d ->
                DraftStorage.delete(this, d.id)
                Toast.makeText(this, getString(R.string.drafts_deleted), Toast.LENGTH_SHORT).show()
                refresh()
            }
        )

        recycler.layoutManager = LinearLayoutManager(this)
        recycler.adapter = draftsAdapter

        refresh()
    }

    override fun onResume() {
        super.onResume()
        SessionManager.touch(this)
    }

    private fun refresh() {
        val drafts = DraftStorage.list(this)
        draftsAdapter.replaceAll(drafts)
        emptyView.visibility = if (drafts.isEmpty()) android.view.View.VISIBLE else android.view.View.GONE
    }
}

