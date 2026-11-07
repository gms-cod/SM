package com.startupmonitor.portdefendertester;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.Editable;
import android.text.TextWatcher;
import android.view.View;
import android.widget.*;

import java.io.IOException;
import java.net.*;
import java.text.SimpleDateFormat;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * Главный экран мобильного стенда тестирования Port Defender & Firewall для Android.
 */
public class MainActivity extends Activity {

    private Spinner spnProtocol;
    private EditText edtIp, edtPort, edtRate, edtTotal;
    private RadioGroup rgMode;
    private RadioButton rbStress, rbSweep, rbProbe;
    private Button btnStart, btnStop, btnExport;
    private TextView txtCalculatedDuration, badgeBanDetected, txtLog;
    private TextView txtCountProgress, txtCountSuccess, txtCountRefused, txtCountTimeout;
    private ProgressBar progressBar;

    private ExecutorService executor;
    private final AtomicBoolean isRunning = new AtomicBoolean(false);
    private final AtomicInteger successCount = new AtomicInteger(0);
    private final AtomicInteger refusedCount = new AtomicInteger(0);
    private final AtomicInteger timeoutCount = new AtomicInteger(0);
    private final AtomicInteger sentCount = new AtomicInteger(0);
    private final AtomicInteger consecutiveDrops = new AtomicInteger(0);
    private final AtomicBoolean banDetected = new AtomicBoolean(false);

    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private final StringBuilder logBuffer = new StringBuilder();

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);

        initViews();
        setupListeners();
        updateCalculatedDuration();
    }

    private void initViews() {
        spnProtocol = findViewById(R.id.spnProtocol);
        edtIp = findViewById(R.id.edtIp);
        edtPort = findViewById(R.id.edtPort);
        edtRate = findViewById(R.id.edtRate);
        edtTotal = findViewById(R.id.edtTotal);

        rgMode = findViewById(R.id.rgMode);
        rbStress = findViewById(R.id.rbStress);
        rbSweep = findViewById(R.id.rbSweep);
        rbProbe = findViewById(R.id.rbProbe);

        btnStart = findViewById(R.id.btnStart);
        btnStop = findViewById(R.id.btnStop);
        btnExport = findViewById(R.id.btnExport);

        txtCalculatedDuration = findViewById(R.id.txtCalculatedDuration);
        badgeBanDetected = findViewById(R.id.badgeBanDetected);
        txtLog = findViewById(R.id.txtLog);

        txtCountProgress = findViewById(R.id.txtCountProgress);
        txtCountSuccess = findViewById(R.id.txtCountSuccess);
        txtCountRefused = findViewById(R.id.txtCountRefused);
        txtCountTimeout = findViewById(R.id.txtCountTimeout);
        progressBar = findViewById(R.id.progressBar);

        ArrayAdapter<String> adapter = new ArrayAdapter<>(this,
                android.R.layout.simple_spinner_dropdown_item, new String[]{"TCP", "UDP"});
        spnProtocol.setAdapter(adapter);
    }

    private void setupListeners() {
        TextWatcher watcher = new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int start, int count, int after) {}
            @Override public void onTextChanged(CharSequence s, int start, int before, int count) { updateCalculatedDuration(); }
            @Override public void afterTextChanged(Editable s) {}
        };

        edtRate.addTextChangedListener(watcher);
        edtTotal.addTextChangedListener(watcher);
        edtPort.addTextChangedListener(watcher);

        spnProtocol.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            @Override public void onItemSelected(AdapterView<?> p, View v, int pos, long id) { updateCalculatedDuration(); }
            @Override public void onNothingSelected(AdapterView<?> p) {}
        });

        rgMode.setOnCheckedChangeListener((g, checkedId) -> {
            if (checkedId == R.id.rbSweep) {
                if (edtPort.getText().toString().equals("445") || edtPort.getText().toString().equals("80")) {
                    edtPort.setText("135,139,445,3389,80,443");
                }
            } else if (checkedId == R.id.rbProbe) {
                edtIp.setText("1.1.1.1");
                edtPort.setText("80");
                edtRate.setText("1");
                edtTotal.setText("3");
            } else {
                if (edtPort.getText().toString().contains(",")) {
                    edtPort.setText("445");
                }
            }
            updateCalculatedDuration();
        });

        findViewById(R.id.btnPresetSmb).setOnClickListener(v -> setPreset("445", "TCP"));
        findViewById(R.id.btnPresetRdp).setOnClickListener(v -> setPreset("3389", "TCP"));
        findViewById(R.id.btnPresetRpc).setOnClickListener(v -> setPreset("135", "TCP"));
        findViewById(R.id.btnPresetHttp).setOnClickListener(v -> setPreset("80", "TCP"));
        findViewById(R.id.btnPresetDns).setOnClickListener(v -> setPreset("53", "UDP"));
        findViewById(R.id.btnPresetWan).setOnClickListener(v -> {
            edtIp.setText("1.1.1.1");
            edtPort.setText("80");
            edtRate.setText("1");
            edtTotal.setText("3");
            spnProtocol.setSelection(0);
            rbProbe.setChecked(true);
            updateCalculatedDuration();
        });

        btnStart.setOnClickListener(v -> startTest());
        btnStop.setOnClickListener(v -> stopTest());
        btnExport.setOnClickListener(v -> exportReport());
    }

    private void setPreset(String port, String proto) {
        edtPort.setText(port);
        spnProtocol.setSelection("UDP".equalsIgnoreCase(proto) ? 1 : 0);
        rbStress.setChecked(true);
        updateCalculatedDuration();
    }

    private void updateCalculatedDuration() {
        try {
            int rate = Integer.parseInt(edtRate.getText().toString().trim());
            int total = Integer.parseInt(edtTotal.getText().toString().trim());
            if (rate > 0 && total > 0) {
                double sec = (double) total / rate;
                String proto = spnProtocol.getSelectedItem() != null ? spnProtocol.getSelectedItem().toString() : "TCP";
                txtCalculatedDuration.setText(String.format(Locale.getDefault(),
                        "Расчётное время: %.1f сек (%d попыток при %d поп/сек, %s)", sec, total, rate, proto));
                return;
            }
        } catch (Exception ignored) {}
        txtCalculatedDuration.setText("Укажите корректные параметры (Rate > 0, Total > 0)");
    }

    private List<Integer> parsePorts(String input) {
        List<Integer> list = new ArrayList<>();
        String[] parts = input.split("[,;\\s]+");
        for (String p : parts) {
            try {
                if (p.contains("-")) {
                    String[] r = p.split("-");
                    int start = Integer.parseInt(r[0].trim());
                    int end = Integer.parseInt(r[1].trim());
                    for (int i = Math.min(start, end); i <= Math.max(start, end) && list.size() < 300; i++) {
                        if (i >= 1 && i <= 65535) list.add(i);
                    }
                } else {
                    int val = Integer.parseInt(p.trim());
                    if (val >= 1 && val <= 65535) list.add(val);
                }
            } catch (Exception ignored) {}
        }
        if (list.isEmpty()) list.add(445);
        return list;
    }

    private void startTest() {
        if (isRunning.get()) return;

        final String targetIpStr = edtIp.getText().toString().trim();
        final List<Integer> targetPorts = parsePorts(edtPort.getText().toString().trim());
        final int rate;
        final int total;

        try {
            rate = Integer.parseInt(edtRate.getText().toString().trim());
            total = Integer.parseInt(edtTotal.getText().toString().trim());
            if (rate <= 0 || total <= 0) throw new NumberFormatException();
        } catch (Exception ex) {
            Toast.makeText(this, "Проверьте числовые параметры Rate и Total!", Toast.LENGTH_SHORT).show();
            return;
        }

        final boolean isUdp = "UDP".equalsIgnoreCase(spnProtocol.getSelectedItem().toString());
        final String protoName = isUdp ? "UDP" : "TCP";

        isRunning.set(true);
        banDetected.set(false);
        consecutiveDrops.set(0);
        successCount.set(0);
        refusedCount.set(0);
        timeoutCount.set(0);
        sentCount.set(0);

        badgeBanDetected.setVisibility(View.GONE);
        btnStart.setEnabled(false);
        btnStop.setEnabled(true);
        progressBar.setMax(total);
        progressBar.setProgress(0);

        logBuffer.setLength(0);
        appendLog("=== Старт мобильного теста: " + targetIpStr + " (" + protoName + ") | Скорость: " + rate + "/сек | Всего: " + total + " ===");

        executor = Executors.newFixedThreadPool(Math.min(32, Math.max(4, rate)));

        new Thread(() -> {
            long startTime = System.currentTimeMillis();
            long intervalMs = Math.max(1, 1000 / rate);

            for (int i = 1; i <= total && isRunning.get(); i++) {
                final int attemptIndex = i;
                final int port = targetPorts.get((attemptIndex - 1) % targetPorts.size());

                executor.submit(() -> {
                    if (!isRunning.get()) return;
                    if (isUdp) {
                        probeUdp(targetIpStr, port, attemptIndex);
                    } else {
                        probeTcp(targetIpStr, port, attemptIndex);
                    }
                });

                int sent = sentCount.incrementAndGet();
                if (sent % Math.max(1, rate / 5) == 0 || sent == total) {
                    mainHandler.post(() -> updateUi(total));
                }

                try {
                    Thread.sleep(intervalMs);
                } catch (InterruptedException ignored) {
                    break;
                }
            }

            try { Thread.sleep(500); } catch (Exception ignored) {}

            isRunning.set(false);
            long elapsed = System.currentTimeMillis() - startTime;
            mainHandler.post(() -> {
                btnStart.setEnabled(true);
                btnStop.setEnabled(false);
                updateUi(total);
                appendLog(String.format(Locale.getDefault(),
                        "=== Завершено за %.2f сек | Отправлено: %d | Подключено: %d | Блокировано/Drop: %d ===",
                        elapsed / 1000.0, sentCount.get(), successCount.get(), refusedCount.get() + timeoutCount.get()));
            });
        }).start();
    }

    private void probeTcp(String ipStr, int port, int index) {
        Socket socket = new Socket();
        try {
            SocketAddress address = new InetSocketAddress(ipStr, port);
            socket.connect(address, 350);
            successCount.incrementAndGet();
            consecutiveDrops.set(0);
            if (index <= 5 || index % 20 == 0) {
                appendLog("[#" + index + "] TCP " + ipStr + ":" + port + " -> ОТКРЫТ (Connected)");
            }
        } catch (SocketTimeoutException ste) {
            timeoutCount.incrementAndGet();
            onDropDetected(index, "Таймаут (Drop / Фильтрация)");
        } catch (ConnectException ce) {
            refusedCount.incrementAndGet();
            if (index <= 5 || index % 20 == 0) {
                appendLog("[#" + index + "] TCP " + ipStr + ":" + port + " -> Отказ (Refused)");
            }
        } catch (Exception ex) {
            refusedCount.incrementAndGet();
            if (ex instanceof NoRouteToHostException) {
                onDropDetected(index, "Нет маршрута к хосту (Блокировка)");
            } else if (index <= 5 || index % 20 == 0) {
                appendLog("[#" + index + "] TCP " + ipStr + ":" + port + " -> Ошибка: " + ex.getMessage());
            }
        } finally {
            try { socket.close(); } catch (IOException ignored) {}
        }
    }

    private void probeUdp(String ipStr, int port, int index) {
        try (DatagramSocket socket = new DatagramSocket()) {
            socket.setSoTimeout(350);
            byte[] data = ("SMON_MOBILE_" + index).getBytes();
            InetAddress address = InetAddress.getByName(ipStr);
            DatagramPacket packet = new DatagramPacket(data, data.length, address, port);
            socket.send(packet);
            successCount.incrementAndGet();
            if (index <= 5 || index % 20 == 0) {
                appendLog("[#" + index + "] UDP " + ipStr + ":" + port + " -> Отправлен датаграмм");
            }
        } catch (Exception ex) {
            refusedCount.incrementAndGet();
            if (index <= 5 || index % 20 == 0) {
                appendLog("[#" + index + "] UDP " + ipStr + ":" + port + " -> Ошибка: " + ex.getMessage());
            }
        }
    }

    private void onDropDetected(int index, String reason) {
        int drops = consecutiveDrops.incrementAndGet();
        if (drops >= 3 && !banDetected.get()) {
            banDetected.set(true);
            mainHandler.post(() -> {
                badgeBanDetected.setVisibility(View.VISIBLE);
                appendLog("🚨 [#" + index + "] ОБНАРУЖЕН БАН: Зафиксирована серия отбрасывания пакетов! Port Defender заблокировал телефон.");
            });
        }
        if (index <= 5 || index % 20 == 0) {
            appendLog("[#" + index + "] " + reason);
        }
    }

    private void updateUi(int total) {
        int sent = sentCount.get();
        progressBar.setProgress(sent);
        txtCountProgress.setText(sent + " / " + total);
        txtCountSuccess.setText(String.valueOf(successCount.get()));
        txtCountRefused.setText(String.valueOf(refusedCount.get()));
        txtCountTimeout.setText(String.valueOf(timeoutCount.get()));
    }

    private void stopTest() {
        isRunning.set(false);
        if (executor != null) executor.shutdownNow();
        btnStop.setEnabled(false);
        btnStart.setEnabled(true);
        appendLog("⏹ Тестирование прервано пользователем.");
    }

    private void appendLog(String msg) {
        String time = new SimpleDateFormat("HH:mm:ss.SSS", Locale.getDefault()).format(new Date());
        String line = "[" + time + "] " + msg + "\n";
        logBuffer.append(line);

        mainHandler.post(() -> {
            txtLog.append(line);
            // Автопрокрутка вниз
            final int scrollAmount = txtLog.getLayout() != null ?
                    txtLog.getLayout().getLineTop(txtLog.getLineCount()) - txtLog.getHeight() : 0;
            if (scrollAmount > 0) txtLog.scrollTo(0, scrollAmount);
        });
    }

    private void exportReport() {
        Intent sendIntent = new Intent(Intent.ACTION_SEND);
        sendIntent.setType("text/plain");
        sendIntent.putExtra(Intent.EXTRA_SUBJECT, "Port Defender Mobile Test Report");
        sendIntent.putExtra(Intent.EXTRA_TEXT, "=== ОТЧЁТ ТЕСТИРОВАНИЯ PORT DEFENDER (ANDROID) ===\n" +
                "Дата: " + new Date() + "\n" +
                "Цель: " + edtIp.getText() + ":" + edtPort.getText() + "\n" +
                "Отправлено: " + sentCount.get() + "\n" +
                "Успешно: " + successCount.get() + "\n" +
                "Сброшено/Drop: " + (refusedCount.get() + timeoutCount.get()) + "\n" +
                "Бан зафиксирован: " + (banDetected.get() ? "ДА" : "НЕТ") + "\n\n" +
                "ЖУРНАЛ СОКЕТОВ:\n" + logBuffer.toString());

        startActivity(Intent.createChooser(sendIntent, "Поделиться отчётом"));
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();
        stopTest();
    }
}

