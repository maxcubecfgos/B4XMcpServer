B4X Development Master Skill
1. Core Philosophy
B4X is a suite of rapid application development (RAD) tools for creating native applications across multiple platforms:

B4A → Android

B4i → iOS

B4J → Desktop (Windows, Mac, Linux, Raspberry Pi)

B4R → Arduino / ESP8266

Key Principle: Code reuse of 70-95%. Develop once in B4J (for faster debugging), then adapt for B4A and B4i.

2. The B4XPages Framework (MANDATORY)
B4XPages solves Android Activity life-cycle issues, making B4A behave like B4J/B4i. Always use B4XPages for new projects.

Project Structure
text
/MyProject/
├── B4A/          # B4A-specific files
├── B4i/          # B4i-specific files
├── B4J/          # B4J-specific files
└── Shared Files/ # Cross-platform layouts, images, etc.
B4XPage Lifecycle Events
Event	When Called
B4XPage_Created	Once, before page becomes visible
B4XPage_Appear	Whenever page becomes visible
B4XPage_Disappear	When page disappears
B4XPage_Background	App moves to background
B4XPage_Foreground	App moves to foreground
B4XPage_CloseRequest	User tries to close page (Back key in B4A, close button in B4J)
B4XPage_Resize	Page is resized (B4i/B4J)
B4XPage Methods (Key Ones)
b4x
'Add page
B4XPages.AddPage("Page1", Page1)

'Show page (navigate)
B4XPages.ShowPage("Page1")

'Close current page
B4XPages.ClosePage(Me)

'Set title
B4XPages.SetTitle(Me, "My Title")

'Get page instance
Private MP As B4XMainPage = B4XPages.GetPage("MainPage")

'Get page ID
Private ID As String = B4XPages.GetPageId(Page1)
3. XUI Library (Cross-Platform Foundation)
Required Declaration
b4x
Private xui As XUI
B4XView (Cross-Platform View Type)
Use B4XView instead of platform-specific views (Panel, Pane, etc.)

Key Properties/Methods:

b4x
'Color
myView.Color = xui.Color_Red
myView.SetColorAndBorder(BackgroundColor, BorderWidth, BorderColor, CornerRadius)

'Text
myView.Text = "Hello"
myView.TextColor = xui.Color_Blue
myView.TextSize = 16

'Layout
myView.Width = 200dip
myView.Height = 100dip
myView.Left = 10dip
myView.Top = 10dip

'Visibility
myView.Visible = True
myView.Enabled = True
myView.SetVisibleAnimated(500, False)  'Fade out

'Rotation
myView.Rotation = 45
myView.SetRotationAnimated(1000, 90)

'Parent/Children
myView.Parent
myView.NumberOfViews
myView.GetView(0)
myView.RemoveAllViews
myView.LoadLayout("LayoutName")

'Snapshots
Dim bmp As B4XBitmap = myView.Snapshot
B4XCanvas (Cross-Platform Drawing)
b4x
Private cvs As B4XCanvas
Private pnl As B4XView

cvs.Initialize(pnl)

'Draw shapes
cvs.DrawLine(x1, y1, x2, y2, xui.Color_Red, 3)
cvs.DrawRect(rect, xui.Color_Blue, True, 0)
cvs.DrawCircle(cx, cy, radius, xui.Color_Green, False, 2)
cvs.DrawText("Hello", x, y, font, xui.Color_Black, "LEFT")
cvs.DrawTextRotated("Hello", x, y, font, xui.Color_Black, "LEFT", 45)

'Draw bitmap
cvs.DrawBitmap(bitmap, destRect)

'Path operations
cvs.ClipPath(path)
cvs.DrawPath(path, color, True, 0)
cvs.RemoveClip

'Must call to update
cvs.Invalidate
cvs.Release()  'When no longer needed
B4XBitmap (Cross-Platform Image)
b4x
Private bmp As B4XBitmap

'Load
bmp = xui.LoadBitmap(File.DirAssets, "image.jpg")
bmp = xui.LoadBitmapResize(File.DirAssets, "image.jpg", 200, 200, True)

'Manipulate
Dim cropped As B4XBitmap = bmp.Crop(left, top, width, height)
Dim resized As B4XBitmap = bmp.Resize(100, 100, True)
Dim rotated As B4XBitmap = bmp.Rotate(90)

'Save
Dim Out As OutputStream
Out = File.OpenOutput(xui.DefaultFolder, "image.png", False)
bmp.WriteToStream(Out, 100, "PNG")
Out.Close
B4XFont (Cross-Platform Font)
b4x
Private font As B4XFont

'Default fonts
font = xui.CreateDefaultFont(16)
font = xui.CreateDefaultBoldFont(16)

'FontAwesome (icons start with 0xF)
font = xui.CreateFontAwesome(20)

'Material Icons (icons start with 0xE)
font = xui.CreateMaterialIcons(20)

'Custom font
font = xui.CreateFont(SomeFont, 20)
B4XRect (Cross-Platform Rectangle)
b4x
Private rect As B4XRect
rect.Initialize(left, top, right, bottom)
'Properties: Left, Top, Right, Bottom, Width, Height, CenterX, CenterY
B4XPath (Cross-Platform Path)
b4x
Private path As B4XPath

'Create path
path.Initialize(x, y)
path.LineTo(x, y)

'Special paths
path.InitializeArc(cx, cy, radius, startAngle, sweepAngle)
path.InitializeOval(rect)
path.InitializeRoundedRect(rect, cornerRadius)
XUI Process Object Methods
b4x
'Colors
xui.Color_Red, xui.Color_Blue, xui.Color_Green, etc.
xui.Color_RGB(r, g, b)
xui.Color_ARGB(a, r, g, b)

'Dialogs (non-modal, use with Wait For)
Dim sf As Object = xui.MsgboxAsync("Message", "Title")
Wait For (sf) Msgbox_Result (Result As Int)

Dim sf2 As Object = xui.Msgbox2Async("Message", "Title", "Yes", "Cancel", "No", Null)
Wait For (sf2) Msgbox_Result (Result As Int)
'Result: xui.DialogResponse_Positive, _Negative, _Cancel

'File handling
xui.DefaultFolder  'B4A=DirInternal, B4i=DirDocuments, B4J=DirData
xui.SetDataFolder("AppName")  'Required for B4J
xui.FileUri(dir, filename)  'URL encode for WebView

'Platform detection
xui.IsB4A, xui.IsB4i, xui.IsB4J

'Scale
xui.Scale  'Screen normalized scale (1 in B4i/B4J)
Cross-Platform View Compatibility
Platform View	Cross-Platform Equivalent
Button, Label, Panel, ImageView	B4XView
ListView (B4A), TableView (B4i)	xCustomListView
ComboBox/Spinner/Picker	B4XComboBox
CheckBox (B4A), Switch (B4i/B4J)	B4XSwitch
SeekBar/Slider	B4XSeekbar
4. The As Keyword (Inline Casting)
b4x
'Cast B4XView to platform-specific type
myView.As(Label).Padding = Array As Int(5dip, 0, 5dip, 0)
myView.As(Button).Tag = 1

'Cast Object to specific type
Dim btn As Button = Sender
'Alternative:
Select Sender.As(Button).Tag

'Cast for JavaObject access
Button1.As(JavaObject).RunMethod("setMouseTransparent", Array(True))
5. Language Fundamentals
Variable Types
Type	Size	Range
Boolean	-	True/False
Byte	8-bit	-128 to 127
Short	16-bit	-32768 to 32767
Int	32-bit	-2,147,483,648 to 2,147,483,647
Long	64-bit	-9.22e18 to 9.22e18
Float	32-bit	1.4e-45 to 3.4e38
Double	64-bit	4.9e-324 to 1.79e308
String	-	Variable length
Char	-	Single character
Variable Declaration
b4x
'Simple variables
Private name As String = "John"
Private age As Int = 30
Private price As Double = 99.95
Private flag As Boolean = True

'Multiple variables
Private a, b, c As Int
Private name, address, city As String

'Constants
Private Const MAX_SIZE As Int = 100
Public Const APP_NAME As String = "MyApp"

'Arrays
Private names(10) As String
Private matrix(3, 3) As Double
Private data(2, 5, 10) As Int

'Array literal
Private days() As String = Array As String("Mon", "Tue", "Wed")

'Types (structures)
Type Person(FirstName As String, LastName As String, Age As Int)
Private user As Person
user.FirstName = "John"
Variable Scope
Scope	Where	Accessibility
Process Global	Sub Process_Globals	All modules (use module name as prefix)
Activity Global	Sub Globals (B4A only)	Current activity only
Class Global	Sub Class_Globals	Current class
Local	Inside Sub	Current Sub only
B4A Special:

Process variables in B4XMainPage/Main's Process_Globals (public) — the Starter service is deprecated as of v13.5, see section 7

Activity variables in each Activity's Globals (legacy Activities-based projects only; use B4XPages for new projects)

Views cannot be process variables (memory leak risk)

Operators
Mathematical: +, -, *, /, Mod, Power(x,y)

Relational: =, <>, >, <, >=, <=

Boolean: And, Or, Not

String Concatenation: & (NOT +)

Control Structures
If-Then-Else:

b4x
If condition Then
    'code
Else If condition2 Then
    'code
Else
    'code
End If

'Inline
If condition Then a = 1 Else a = 0

'IIf - conditional expression, useful inline (e.g. inside method calls)
Dim a As Int = IIf(condition, 1, 0)
Label1.Text = IIf(score >= 60, "Pass", "Fail")
Select-Case:

b4x
Select value
    Case 1, 2, 3
        'code
    Case 4, 5
        'code
    Case Else
        'code
End Select
Loops:

b4x
'For-Next
For i = 0 To 10
    'code
Next

'Step
For i = 10 To 0 Step -1
    'code
Next

'For-Each
For Each item As String In list
    'code
Next

'Do While
Do While condition
    'code
Loop

'Do Until
Do Until condition
    'code
Loop
Subroutines (Subs)
b4x
'Simple Sub
Sub MySub
    'code
End Sub

'With parameters
Sub CalcTotal(price As Double, tax As Double) As Double
    Return price * (1 + tax)
End Sub

'With array parameter
Sub ProcessData(values() As Int)
    'code
End Sub

'Calling
Dim result As Double = CalcTotal(100, 0.08)

Initialized / NotInitialized (Modern Object Check)
b4x
'Old, verbose pattern
If Map1 <> Null And Map1.IsInitialized Then
    'code
End If

'Current, preferred syntax (B4A v13.3+)
If Initialized(Map1) Then
    'code
End If

If NotInitialized(Map1) Then
    Map1.Initialize
End If
6. Resumable Subs (Async Programming)
Any Sub with Sleep or Wait For is resumable. Essential for async operations.

Sleep
b4x
Sub UpdateUI
    For i = 1 To 100
        Label1.Text = i
        Sleep(100)  'Pause 100ms, UI updates
    Next
End Sub
Wait For (Event Handling)
b4x
'Basic usage
Sub DownloadImage(url As String, iv As ImageView)
    Dim job As HttpJob
    job.Initialize("", Me)
    job.Download(url)
    Wait For (job) JobDone(job As HttpJob)  'Wait for event
    If job.Success Then
        iv.Bitmap = job.GetBitmap
    End If
    job.Release
End Sub
Wait For with Sender Filter (Recommended)
b4x
'Always use sender filter to avoid event collision
Dim sf As Object = xui.Msgbox2Async("Delete?", "Title", "Yes", "", "No", Null)
Wait For (sf) Msgbox_Result (Result As Int)
If Result = xui.DialogResponse_Positive Then
    'Delete
End If
ResumableSub Return Value
b4x
Sub Button1_Click
    Wait For (Sum(1, 2)) Complete (Result As Int)
    Log("Result: " & Result)
End Sub

Sub Sum(a As Int, b As Int) As ResumableSub
    Sleep(100)
    Return a + b
End Sub
7. Database (SQLite)
Initialization
B4XPages:

b4x
#If B4J
    xui.SetDataFolder("MyApp")
    SQL1.InitializeSQLite(xui.DefaultFolder, "mydb.db", True)
#Else
    SQL1.Initialize(xui.DefaultFolder, "mydb.db", True)
#End If

'Copy from Assets if needed
If File.Exists(xui.DefaultFolder, "mydb.db") = False Then
    File.Copy(File.DirAssets, "mydb.db", xui.DefaultFolder, "mydb.db")
End If
B4A (legacy Starter Service — deprecated, see note below):

b4x
Sub Process_Globals
    Public SQL1 As SQL
End Sub

Sub Service_Create
    SQL1.Initialize(File.DirInternal, "mydb.db", True)
End Sub

⚠️ Starter Service Deprecated (B4A v13.5+): Google's background restrictions make the Starter service increasingly unreliable, and B4XPages projects don't need it. New projects should declare shared/process-global objects (like SQL1) directly in the B4XMainPage or Main module's Process_Globals instead, and initialize them in B4XPage_Created or an equivalent startup point. Application_Error can now be added directly to the Main module when the Starter service is excluded. Prefer the B4XPages initialization pattern shown above for all new code.
Database Operations
Create Table:

b4x
SQL1.ExecNonQuery("CREATE TABLE IF NOT EXISTS users (ID INTEGER PRIMARY KEY, Name TEXT, Age INTEGER)")
Insert:

b4x
'Parameterized query (ALWAYS use this!)
SQL1.ExecNonQuery2("INSERT INTO users VALUES (?, ?, ?)", Array As String(Null, "John", "30"))
Update:

b4x
SQL1.ExecNonQuery2("UPDATE users SET Age = ? WHERE Name = ?", Array As String("31", "John"))
Select:

b4x
Dim rs As ResultSet = SQL1.ExecQuery2("SELECT * FROM users WHERE Age > ?", Array As String("25"))
Do While rs.NextRow
    Dim id As Long = rs.GetLong("ID")
    Dim name As String = rs.GetString("Name")
    Dim age As Int = rs.GetInt("Age")
Loop
rs.Close
Delete:

b4x
SQL1.ExecNonQuery2("DELETE FROM users WHERE ID = ?", Array As String(id))
Best Practices
ALWAYS use parameterized queries (ExecQuery2, ExecNonQuery2)

Use ResultSet (cross-platform) not Cursor (B4A only)

Use BeginTransaction/EndTransaction for bulk inserts (10x faster)

b4x
SQL1.BeginTransaction
Try
    For i = 1 To 1000
        SQL1.ExecNonQuery2("INSERT INTO users VALUES (?, ?, ?)", Array As String(Null, "User" & i, Rnd(18,65)))
    Next
    SQL1.TransactionSuccessful
Catch
    Log(LastException.Message)
End Try
SQL1.EndTransaction
Async Queries (Resumable Subs)
b4x
'Batch insert
For i = 1 To 1000
    SQL1.AddNonQueryToBatch("INSERT INTO users VALUES (?, ?, ?)", Array As String(Null, "User" & i, Rnd(18,65)))
Next
Dim sf As Object = SQL1.ExecNonQueryBatch("SQL")
Wait For (sf) SQL_NonQueryComplete (Success As Boolean)

'Query
Dim sf2 As Object = SQL1.ExecQueryAsync("SQL", "SELECT * FROM users", Null)
Wait For (sf2) SQL_QueryComplete (Success As Boolean, rs As ResultSet)
If Success Then
    Do While rs.NextRow
        'Process
    Loop
    rs.Close
End If
DBUtils Library (Helper)
Common functions:

CopyDBFromAssets(FileName) As String - Copy DB from assets to writable folder

ExecuteMemoryTable(SQL, Query, Args, Limit) As List - Query results as List of arrays

ExecuteMap(SQL, Query, Args) As Map - Single record as Map

InsertMaps(SQL, Table, ListOfMaps) - Insert multiple records

CreateTable(SQL, TableName, FieldsAndTypes, PrimaryKey)

TableExists(SQL, TableName) As Boolean

GetTables(SQL) As List

8. Files I/O
File Locations (Cross-Platform)
b4x
xui.SetDataFolder("MyApp")
Dim defaultFolder As String = xui.DefaultFolder
'B4A: File.DirInternal
'B4i: File.DirDocuments
'B4J: File.DirData
File Operations
b4x
'Check existence
If File.Exists(dir, filename) Then

'Read text
Dim text As String = File.ReadString(dir, filename)
Dim list As List = File.ReadList(dir, filename)
Dim map As Map = File.ReadMap(dir, filename)

'Write text
File.WriteString(dir, filename, text)
File.WriteList(dir, filename, list)
File.WriteMap(dir, filename, map)

'Binary
Dim data() As Byte = File.ReadBytes(dir, filename)
File.WriteBytes(dir, filename, data)

'Copy
File.Copy(srcDir, srcFile, dstDir, dstFile)

'Delete
File.Delete(dir, filename)

'List files
Dim files As List = File.ListFiles(dir)

'Create directory
File.MakeDir(parentDir, "subfolder")
TextReader/TextWriter (For custom encoding)
b4x
'Read with specific encoding
Dim tr As TextReader
tr.Initialize2(File.OpenInput(dir, filename), "Windows-1252")
Dim text As String = tr.ReadAll
tr.Close

'Write with specific encoding
Dim tw As TextWriter
tw.Initialize2(File.OpenOutput(dir, filename, False), "Windows-1252")
tw.WriteLine("Hello")
tw.Close
9. Collections
Lists (Dynamic Arrays)
b4x
Private list As List
list.Initialize

'Add
list.Add(item)
list.AddAll(array)
list.InsertAt(index, item)

'Get
Dim item As Object = list.Get(index)
Dim size As Int = list.Size

'Remove
list.RemoveAt(index)

'Iterate
For i = 0 To list.Size - 1
    Dim item As Object = list.Get(i)
Next

For Each item As Object In list
    'Process
Next

'Sort
list.Sort(True)  'Ascending
list.Sort(False) 'Descending
Maps (Key-Value Pairs)
b4x
Private map As Map
map.Initialize

'Add
map.Put("key", value)

'Get
Dim value As Object = map.Get("key")

'Check
If map.ContainsKey("key") Then

'Remove
map.Remove("key")

'Iterate
For Each key As String In map.Keys
    Dim value As Object = map.Get(key)
Next

'Size
Dim size As Int = map.Size
Important: Maps do NOT preserve order in general. Use B4XOrderedMap if order matters.

B4XCollections Helper Methods (current)
b4x
'One-line empty collections (avoid the old Dim + Initialize boilerplate when returning defaults)
Dim l As List = CreateList("a", "b", "c")   'shortcut for Initialize + AddAll
Dim empty As List = EmptyList
Dim emptyM As Map = EmptyMap

'Merge without manual loops
Dim merged As List = MergeLists(list1, list2)
Dim mergedMap As Map = MergeMaps(map1, map2)

'Thread-safe variants (useful with multi-threaded/async code)
Dim safeList As List = CopyOnWriteList
Dim safeMap As Map = CopyOnWriteMap

10. Strings
String Methods
b4x
Dim s As String = "Hello World"

s.Length
s.CharAt(0)  'H
s.SubString(0)  'Hello World
s.SubString2(0, 5)  'Hello
s.IndexOf("l")  '2
s.LastIndexOf("l")  '3
s.Contains("World")  'True
s.StartsWith("He")  'True
s.EndsWith("ld")  'True
s.Replace("World", "Universe")  'Hello Universe
s.ToLowerCase  'hello world
s.ToUpperCase  'HELLO WORLD
s.Trim  'Remove leading/trailing spaces
s.EqualsIgnoreCase("hello world")  'True
s.GetBytes("UTF-8")
Smart Strings (Multi-line & Interpolation)
b4x
'Multi-line without escaping quotes
Dim query As String = $"SELECT * FROM users
WHERE age > 18
ORDER BY name"$

'Interpolation
Dim name As String = "John"
Dim age As Int = 30
Log($"Hello {name}, you are {age} years old"$)

'Number formatting
Log($"Value: ${0.2}(123.456)"$)  'Value: 123.46
Log($"ID: ${3}(5)"$)  'ID: 005

'Date formatting
Log($"Today: $date{DateTime.Now}"$)
Log($"Time: $time{DateTime.Now}"$)
StringBuilder (For many concatenations)
b4x
Private sb As StringBuilder
sb.Initialize
sb.Append("First line").Append(CRLF)
sb.Append("Second line")
Dim result As String = sb.ToString
CSBuilder (Rich Text Formatting)
b4x
Private cs As CSBuilder
cs.Initialize
cs.Color(xui.Color_Red).Append("Red text")
cs.Bold.Append(" Bold text")
cs.Font(Typeface.FONTAWESOME, 20).Append(Chr(0xF209))
cs.PopAll
Label1.Text = cs
11. CustomViews
CustomView (XUI) Class Structure
b4x
#DesignerProperty: Key: Max, DisplayName: Max Value, FieldType: Int, DefaultValue: 100
#Event: ValueChanged(Value As Int)

Sub Class_Globals
    Private mEventName As String 'ignore
    Private mCallBack As Object 'ignore
    Public mBase As B4XView 'ignore
    Private xui As XUI 'ignore
    'Your variables
End Sub

Public Sub Initialize(Callback As Object, EventName As String)
    mEventName = EventName
    mCallBack = Callback
End Sub

Public Sub DesignerCreateView(Base As Object, Lbl As Label, Props As Map)
    mBase = Base
    mBase.Tag = Me
    'Get designer properties
    Dim maxValue As Int = Props.GetDefault("Max", 100)
End Sub

'For code-based creation
Public Sub AddToParent(Parent As B4XView, Left As Int, Top As Int, Width As Int, Height As Int)
    mBase = xui.CreatePanel("mBase")
    Parent.AddView(mBase, Left, Top, Width, Height)
    mBase.Tag = Me
    'Initialize
End Sub

'Raise external event
Private Sub SomeAction
    If xui.SubExists(mCallBack, mEventName & "_ValueChanged", 1) Then
        CallSubDelayed2(mCallBack, mEventName & "_ValueChanged", mCurrentValue)
    End If
End Sub
Creating b4xlib Library
Create manifest.txt:

text
Version=1.0
Author=YourName
B4J.DependsOn=jXUI
B4A.DependsOn=XUI
B4i.DependsOn=iXUI
Zip with .b4xlib extension

Copy to AdditionalLibraries\B4X\

Refresh libraries list

12. JavaObject / NativeObject (Native APIs)
B4A/B4J JavaObject
b4x
'Access static class
Dim jo As JavaObject
jo.InitializeStatic("android.os.Build")
Log(jo.GetField("MODEL"))

'Create new instance
jo.InitializeNewInstance("java.lang.String", Array("Hello"))

'Run method
Dim result As Object = jo.RunMethod("methodName", Array(arg1, arg2))

'Get/Set field
Dim value As Object = jo.GetField("fieldName")
jo.SetField("fieldName", newValue)

'Wrap existing object
Dim jo As JavaObject = SomeView

'Create event listener
Dim e As Object = jo.CreateEvent("android.view.View.OnTouchListener", "MyEvent", False)
jo.RunMethod("setOnTouchListener", Array(e))
B4i NativeObject
b4x
Dim no As NativeObject

'Initialize class
no.Initialize("NSLocale")

'Run method (include colons)
Dim lang As String = no.RunMethod("preferredLanguages", Null).RunMethod("objectAtIndex:", Array(0)).AsString

'Get/Set field
Dim value As Object = no.GetField("fieldName")
no.SetField("fieldName", value)

'Color conversion
no.ColorToUIColor(Colors.Red)  'B4i color -> UIColor
no.UIColorToColor(uicolor)     'UIColor -> B4i color
13. Common Code Patterns
B4XPages Main Template
b4x
#Region Shared Files
#CustomBuildAction: folders ready, %WINDIR%\System32\Robocopy.exe, "..\..\Shared Files" "..\Files"
#End Region

'Ctrl + click to export as zip: ide://run?File=%B4X%\Zipper.jar&Args=%PROJECT_NAME%.zip

Sub Class_Globals
    Private Root As B4XView
    Private xui As XUI
End Sub

Public Sub Initialize
End Sub

Private Sub B4XPage_Created(Root1 As B4XView)
    Root = Root1
    Root.LoadLayout("MainPage")
End Sub
Cross-Platform Code with Conditional Compilation
b4x
#If B4A
    'Android-specific code
#Else If B4i
    'iOS-specific code
#Else If B4J
    'Java/Desktop-specific code
#End If

'Platform detection at runtime
If xui.IsB4A Then
    'B4A-specific
Else If xui.IsB4i Then
    'B4i-specific
Else If xui.IsB4J Then
    'B4J-specific
End If
14. Best Practices (What to Avoid)
Old/Bad Practice	New/Good Practice
DoEvents	Sleep(0) or Wait For
Msgbox (modal), MsgboxAsync/Msgbox2Async (platform-specific)	xui.MsgboxAsync / xui.Msgbox2Async + Wait For
Custom dialog implementations	B4XDialogs (cross-platform, fully customizable)
Activities (B4A)	B4XPages
Starter Service (B4A, as of v13.5)	Process-global objects declared in B4XMainPage/Main + B4XPage_Created; Application_Error in Main module
ListView (B4A), TableView (B4i)	xCustomListView
CustomListView module	xCustomListView library
Platform-specific views (Node/Pane/Button/EditText/TextField/fx...)	B4XView, B4XCanvas, B4XFloatTextField, XUI
CallSubDelayed for completion	ResumableSub return
CallSubDelayed/CallSubPlus just to defer execution	Sleep(x)
File.DirDefaultExternal, DirRootExternal, DirInternal, DirCache, DirLibrary, DirTemp, DirData	xui.DefaultFolder (or ContentChooser/SaveAs for user-facing files)
Cursor (B4A only)	ResultSet (cross-platform)
Non-parameterized SQL (ExecQuery/ExecNonQuery)	Parameterized queries (ExecQuery2/ExecNonQuery2)
ExecQuerySingleResult when no result is possible	ExecQuery2
Map.GetKeyAt/GetValueAt	For Each key In Map.Keys
Building layouts programmatically	Visual Designer + Anchors + Script
Multiple layout variants per screen size	Anchors + Designer Script (flexible single layout)
Round2 (to format displayed numbers)	NumberFormat / B4XFormatter
TextReader/TextWriter over network streams	AsyncStreams
TextReader/TextWriter for plain UTF-8 files	File.ReadString / File.ReadList
VideoView	ExoPlayer
StartServiceAt / StartServiceAtExact	StartReceiverAt / StartReceiverAtExact
Shared modules folder	Referenced modules
Multiline strings built with & concatenation	Smart strings ($"..."$)
15. Key Design Patterns
DRY (Don't Repeat Yourself)
Use loops, arrays, and classes to avoid duplicating code

Example: Button arrays with shared Click event

b4x
'GOOD
Private Buttons(5) As Button
For i = 0 To 4
    Buttons(i).Initialize("ButtonClick")
    Buttons(i).Tag = i
    Activity.AddView(Buttons(i), 10dip, 10dip + i * 60dip, 150dip, 50dip)
Next

Sub ButtonClick_Click
    Dim btn As Button = Sender
    Log("Button " & btn.Tag & " clicked")
End Sub
Separation of Data and Code
Store data in files or databases, not hard-coded

Use Maps for Configuration
b4x
Dim settings As Map
settings.Put("language", "English")
settings.Put("theme", "Dark")
File.WriteMap(File.DirInternal, "settings.txt", settings)
16. Libraries Management
Folder Structure
text
AdditionalLibraries/
├── B4A/          # B4A JAR + XML files
├── B4i/          # B4i XML files
├── B4J/          # B4J JAR + XML files
├── B4R/          # B4R libraries
├── B4X/          # B4X libraries (*.b4xlib)
└── Snippets/     # Code snippets
Loading a Library
Download library files

Copy to appropriate AdditionalLibraries folder

Right-click in Libraries tab → Refresh

Check the library in the list

17. Quick Reference: Common Tasks
Async Dialog
b4x
Dim sf As Object = xui.Msgbox2Async("Delete?", "Title", "Yes", "Cancel", "No", Null)
Wait For (sf) Msgbox_Result (Result As Int)
If Result = xui.DialogResponse_Positive Then
    'Delete
End If
HTTP Request
b4x
Dim job As HttpJob
job.Initialize("", Me)
job.Download("https://api.example.com/data")
Wait For (job) JobDone(job As HttpJob)
If job.Success Then
    Dim result As String = job.GetString
End If
job.Release
Timer
b4x
Private Timer1 As Timer
Timer1.Initialize("Timer1", 1000)  '1 second
Timer1.Enabled = True

Sub Timer1_Tick
    'Do something
End Sub
Create Panel Programmatically
b4x
Dim pnl As B4XView = xui.CreatePanel("")
pnl.SetColorAndBorder(xui.Color_White, 1, xui.Color_Black, 5)
pnl.SetLayoutAnimated(0, 10dip, 10dip, 200dip, 100dip)
Root.AddView(pnl, 10dip, 10dip, 200dip, 100dip)
18. Module & Project File Structure (Avoiding Corruption)

⚠️ Critical for any tool that edits .bas/.b4a/.b4j/.b4i files programmatically (MCP servers, scripts, AI agents). The header/metadata portion of these files is NOT free-form text — it's a strict key=value format the IDE parses. Getting it wrong silently corrupts the project (module disappears, IDE refuses to open the file, or the visual designer breaks) rather than throwing a compile error you'd notice.

18.1 .bas Module Header Anatomy

Every module file (Class, Service, Code Module) starts with a header block terminated by @EndOfDesignText@. Everything before that line is metadata; everything after is designer text (attributes/properties) followed by the actual code.

b4x
B4A=true                    'Which IDE this module belongs to (B4A/B4J/B4i=true). May be absent in older/hand-made modules — if absent, leave it absent.
Group=Default Group         'Designer group, almost always "Default Group"
ModulesStructureVersion=1   'Internal format version — never change
Type=Class                  'Class | Service | Activity | CodeModule
Version=13.4                'The B4X IDE version that last saved this file, NOT your app version
@EndOfDesignText@
'Designer text / attributes go here (see below), then the actual Sub declarations

Rule: never regenerate this block from scratch. If you need to change something in it, do a targeted line replacement and leave every other line byte-for-byte identical, including the trailing space after "Group" or "Region" if present in the original.

18.2 Type-Specific Designer Text

Service:
b4x
#Region  Service Attributes 
    #StartAtBoot: False
#End Region

Commenting out the whole region (prefixing every line with ') is valid and equivalent to omitting it — you'll see this in real projects.

Custom View (Class) with designer-visible properties:
b4x
'Custom View: SlapToggle
#DesignerProperty: Key: ColorOn, DisplayName: Color On, FieldType: Color, DefaultValue: 0xFF14BDD8
#DesignerProperty: Key: ColorOff, DisplayName: Color Off, FieldType: Color, DefaultValue: 0xFF4B6875
#Event: ValueChanged (Value As Boolean)

The Key: value in #DesignerProperty must match exactly (case-sensitive) a property your class code exposes, or the visual designer silently fails to bind it. Adding a #DesignerProperty line without adding the matching Sub/property in code (or vice versa) is a common corruption source when an AI edits only one side.

18.3 Project Files (.b4a / .b4j / .b4i)

The project file lists every Library, File (asset), and Module with sequentially numbered keys, plus a matching count:
b4x
Library1=b4xcollections
Library2=b4xdrawer
...
Library18=b4xencryption
NumberOfLibraries=18

Module1=BGSearchService
...
Module6=TopBar
NumberOfModules=6

Hard rules:
- The numeric suffix (Library7, Module3...) is an opaque ID, not alphabetical or logical order — never resort or renumber existing entries.
- Numbering must run 1..N with no gaps, where N == NumberOfLibraries / NumberOfFiles / NumberOfModules exactly. Adding a library/module without incrementing the count (or vice versa) corrupts the project.
- Removing a module in code but leaving its ModuleN=Name entry (or removing the entry but not decrementing NumberOfModules and renumbering the rest) breaks the project.
- ManifestCode=... is a single escaped one-line string using ~\n~ as the newline token. Never reformat it into real newlines — the IDE expects the literal ~\n~ sequence.
- #Region Project Attributes / #Region Activity Attributes blocks (e.g. #ApplicationLabel, #VersionCode, #MainFormWidth) live in the project's own designer-text section after its @EndOfDesignText@ — add new attribute lines inside the existing #Region, never create a second one.

18.4 .meta Files — Never Touch

Files like Project.b4a.meta contain pure IDE session state: ModuleBookmarks*, ModuleBreakpoints*, ModuleClosedNodes* (which tree nodes are collapsed in the IDE), NavigationStack (recent-location history), SelectedBuild, VisibleModules. None of this affects compilation. An AI/MCP tool should never write to .meta files — at best it does nothing useful, at worst it desyncs what the IDE shows from what's actually true and looks like corruption to the developer.

18.5 Safe-Editing Checklist for Automated Tools
- Only touch code after @EndOfDesignText@ unless the task specifically requires adding a library, file, module, or designer property.
- When adding a Library/File/Module: append the next sequential number, then increment the matching NumberOf* count in the same edit.
- When removing one: remove its entry, renumber the remaining entries to close the gap, and decrement the count.
- Never modify Version=, ModulesStructureVersion=, or the B4A=/B4J=/B4i= line.
- Never write to .meta files.
- Preserve exact whitespace/trailing spaces in #Region lines — B4X's parser has historically been picky about this.
19. Version Compatibility
B4A: v10.0+ (B4XPages), v13.50 (current)

B4i: v6.80+ (B4XPages), v10.00 (current)

B4J: v8.50+ (B4XPages), v10.5 (current)

B4R: v4.00

Recent language/IDE milestones worth knowing about:
- v13.0 (2024): SDK updated for Android 14 (targetSdkVersion 34), requires Java 19.
- v13.1 (2025): WebView.AllowFileAccess property; expanded #CustomBuildAction variables.
- v13.3 (2025): Initialized/NotInitialized keywords; #Macro attribute; new B4XCollections helpers (EmptyList, EmptyMap, CreateList, MergeLists, MergeMaps, CopyOnWriteList, CopyOnWriteMap).
- v13.4 (2025): New command-line tools and prepackaged SDK.
- v13.5 (2026, latest): Integrated code bundle; Starter service being phased out in favor of B4XPages-native initialization; Application_Error can live directly in the Main module; List.Sublist fast read-only sublist method.

Always check the official changelogs (b4x.com) before starting a new project, since B4X ships frequent incremental updates.

This skill represents the comprehensive B4X development ecosystem. Following these patterns ensures maximum code reuse, maintainability, and cross-platform compatibility.